using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CommentService.Contracts.Comments;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Contracts.Comments.Requests;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

public sealed class GetCommentInboxEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments/inbox", async Task<EndpointResult<IReadOnlyList<InboxCommentDto>>> (
                    [AsParameters] GetCommentInboxRequest request,
                    [FromServices] GetCommentInboxHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCommentInboxQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetCommentInboxQuery(GetCommentInboxRequest Request) : IQuery;

public sealed class GetCommentInboxQueryValidator : AbstractValidator<GetCommentInboxQuery>
{
    public GetCommentInboxQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetCommentInboxRequest.Limit)));
    }
}

public sealed class GetCommentInboxHandler
    : IQueryHandlerWithResult<IReadOnlyList<InboxCommentDto>, GetCommentInboxQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IValidator<GetCommentInboxQuery> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<GetCommentInboxHandler> _logger;

    public GetCommentInboxHandler(
        ITransactionManager transactionManager,
        IAuthServiceClient authServiceClient,
        IEducationContentServiceClient ecsClient,
        IValidator<GetCommentInboxQuery> validator,
        UserScopedData user,
        ILogger<GetCommentInboxHandler> logger)
    {
        _transactionManager = transactionManager;
        _authServiceClient = authServiceClient;
        _ecsClient = ecsClient;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<InboxCommentDto>, Error>> Handle(
        GetCommentInboxQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // .RequirePermissions(Comments.VIEW) on the endpoint guarantees authenticated user
        // → UserId != Guid.Empty by the time we reach the handler.
        Guid userId = _user.UserId;

        // Replies on user's comments — uses ltree containment to walk one level deeper
        // (parent path @> child path AND child.depth = parent.depth + 1).
        // Excludes self-replies and deleted reply rows; deleted parents return null preview.
        const string sql = """
                           SELECT
                               c.id                                                AS Id,
                               CASE WHEN c.is_deleted THEN NULL ELSE c.content END AS Content,
                               c.author_id                                         AS AuthorId,
                               c.depth                                             AS Depth,
                               c.created_at                                        AS CreatedAt,
                               c.updated_at                                        AS UpdatedAt,
                               c.is_deleted                                        AS IsDeleted,
                               c.target_entity_type                                AS TargetEntityType,
                               c.target_entity_id                                  AS TargetEntityId,
                               p.id                                                AS ParentId,
                               p.author_id                                         AS ParentAuthorId,
                               CASE WHEN p.is_deleted THEN NULL ELSE LEFT(p.content, 280) END
                                                                                   AS ParentPreview
                           FROM comments p
                           INNER JOIN comments c
                               ON c.path <@ p.path
                              AND c.depth = p.depth + 1
                              AND c.target_entity_type = p.target_entity_type
                              AND c.target_entity_id = p.target_entity_id
                           WHERE p.author_id = @UserId
                             AND c.author_id <> @UserId
                             AND c.is_deleted = false
                           ORDER BY c.created_at DESC, c.id DESC
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        List<InboxCommentDto> items = (await connection.QueryAsync<InboxCommentDto>(
                new CommandDefinition(
                    sql,
                    new { UserId = userId, query.Request.Limit },
                    cancellationToken: cancellationToken)))
            .ToList();

        if (items.Count > 0)
        {
            await EnrichWithAuthorInfoAsync(items, cancellationToken);
            await EnrichWithCourseBindingAsync(items, cancellationToken);
        }

        return items;
    }

    private async Task EnrichWithAuthorInfoAsync(
        List<InboxCommentDto> items,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> authorIds = items
            .Select(c => c.AuthorId)
            .Distinct()
            .ToList();

        var result = await _authServiceClient.GetUsersByIdsAsync(authorIds, cancellationToken);
        if (result.IsFailure)
        {
            return;
        }

        Dictionary<Guid, AuthUserLookupDto> userById = result.Value.ToDictionary(u => u.UserId);

        for (int i = 0; i < items.Count; i++)
        {
            if (userById.TryGetValue(items[i].AuthorId, out AuthUserLookupDto? user))
            {
                items[i] = items[i] with
                {
                    AuthorName = user.Name,
                    AuthorUsername = user.Username,
                    AuthorAvatarId = user.AvatarId,
                };
            }
        }
    }

    /// <summary>
    /// Резолвит primary course slug для целевых сущностей (material + issue) через
    /// ECS internal endpoints. Material → course_materials, Issue → project →
    /// course_items. Запросы идут параллельно. Если ECS не отвечает — items
    /// отдаются без TargetCourseSlug, фронт показывает карточку без ссылки.
    /// Failure здесь не валит весь inbox.
    /// </summary>
    private async Task EnrichWithCourseBindingAsync(
        List<InboxCommentDto> items,
        CancellationToken cancellationToken)
    {
        Guid[] materialIds = items
            .Where(i => string.Equals(i.TargetEntityType, "material", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.TargetEntityId)
            .Distinct()
            .ToArray();

        Guid[] issueIds = items
            .Where(i => string.Equals(i.TargetEntityType, "issue", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.TargetEntityId)
            .Distinct()
            .ToArray();

        if (materialIds.Length == 0 && issueIds.Length == 0)
        {
            return;
        }

        Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> materialsTask =
            materialIds.Length > 0
                ? _ecsClient.GetMaterialCourseBindingsAsync(materialIds, cancellationToken)
                : Task.FromResult(Result.Success<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>(
                    Array.Empty<MaterialCourseBindingLookupDto>()));

        Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> issuesTask =
            issueIds.Length > 0
                ? _ecsClient.GetIssueCourseBindingsAsync(issueIds, cancellationToken)
                : Task.FromResult(Result.Success<IReadOnlyList<IssueCourseBindingLookupDto>, Error>(
                    Array.Empty<IssueCourseBindingLookupDto>()));

        await Task.WhenAll(materialsTask, issuesTask);

        Dictionary<Guid, string> slugByEntityId = new();

        Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error> materialsResult = await materialsTask;
        if (materialsResult.IsSuccess)
        {
            foreach (MaterialCourseBindingLookupDto b in materialsResult.Value)
            {
                slugByEntityId[b.MaterialId] = b.CourseSlug;
            }
        }
        else
        {
            _logger.LogWarning(
                "Failed to enrich inbox with material course bindings: {ErrorCode}",
                materialsResult.Error.Messages is { Count: > 0 } m ? m[0].Code : "unknown");
        }

        Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error> issuesResult = await issuesTask;
        if (issuesResult.IsSuccess)
        {
            foreach (IssueCourseBindingLookupDto b in issuesResult.Value)
            {
                slugByEntityId[b.IssueId] = b.CourseSlug;
            }
        }
        else
        {
            _logger.LogWarning(
                "Failed to enrich inbox with issue course bindings: {ErrorCode}",
                issuesResult.Error.Messages is { Count: > 0 } m ? m[0].Code : "unknown");
        }

        if (slugByEntityId.Count == 0)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (slugByEntityId.TryGetValue(items[i].TargetEntityId, out string? slug))
            {
                items[i] = items[i] with { TargetCourseSlug = slug };
            }
        }
    }
}
