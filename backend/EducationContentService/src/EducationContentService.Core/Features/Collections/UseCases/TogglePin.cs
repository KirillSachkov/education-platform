using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

// No FluentValidation validator — route constraint {collectionId:guid} enforces non-empty GUID
public sealed record TogglePinCommand(Guid CollectionId, bool IsPinned) : ICommand;

public sealed record TogglePinRequest(bool IsPinned);

public sealed class TogglePinEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("collections/{collectionId:guid}/pin", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromBody] TogglePinRequest request,
                    [FromServices] TogglePinHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new TogglePinCommand(collectionId, request.IsPinned), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class TogglePinHandler : ICommandHandler<Guid, TogglePinCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<TogglePinHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public TogglePinHandler(
        ICollectionsRepository collectionsRepository,
        ITransactionManager transactionManager,
        ILogger<TogglePinHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        TogglePinCommand command,
        CancellationToken cancellationToken)
    {
        Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
            c => c.Id == command.CollectionId, cancellationToken);
        if (collectionResult.IsFailure)
            return collectionResult.Error;

        Collection collection = collectionResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(collection.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        if (command.IsPinned)
        {
            if (collection.CourseId is null)
                return EducationErrors.CannotPinSpaceLevelCollection();

            UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
            if (txResult.IsFailure)
                return txResult.Error;

            SortKey sortKey = await GetNextPinnedSortKeyAsync(
                collection.CourseId.Value, cancellationToken);

            collection.Pin(sortKey);

            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
                return saveResult.Error;

            UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
            if (commitResult.IsFailure)
                return commitResult.Error;
        }
        else
        {
            collection.Unpin();

            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        _logger.LogInformation(
            "User {UserId} toggled pin to {IsPinned} for collection {CollectionId}",
            _userScopedData.UserId, command.IsPinned, collection.Id);

        return collection.Id;
    }

    private async Task<SortKey> GetNextPinnedSortKeyAsync(
        Guid courseId, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT pinned_sort_key
                           FROM collections
                           WHERE course_id = @CourseId
                             AND is_pinned = true
                             AND pinned_sort_key IS NOT NULL
                           ORDER BY pinned_sort_key DESC
                           LIMIT 1
                           FOR UPDATE;
                           """;

        string? lastKey = await connection.QueryFirstOrDefaultAsync<string?>(
            new CommandDefinition(sql, new { CourseId = courseId }, cancellationToken: cancellationToken));

        if (lastKey is null)
            return SortKey.Initial();

        return SortKey.After(SortKey.Create(lastKey).Value);
    }
}
