using System.Data.Common;
using Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Core.Features.Bookmarks.Queries;

public sealed record GetBookmarksQuery(string? Cursor, int Limit, Guid? CourseId, EntityType? EntityType) : IQuery;

public sealed record GetBookmarksQueryParams(string? Cursor, int? Limit, Guid? CourseId, EntityType? EntityType);

public sealed class GetBookmarksQueryValidator : AbstractValidator<GetBookmarksQuery>
{
    public GetBookmarksQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.EntityType)
            .Must(value => value is null || BookmarkEntityReference.IsSupported(value.Value))
            .WithError(GeneralErrors.ValueIsInvalid("entityType"));
    }
}

public sealed class GetBookmarksEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/bookmarks",
                async Task<EndpointResult<CursorResponse<BookmarkedMaterialDto>>> (
                        [AsParameters] GetBookmarksQueryParams queryParams,
                        [FromServices] GetBookmarksHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetBookmarksQuery(
                            queryParams.Cursor,
                            queryParams.Limit ?? 20,
                            queryParams.CourseId,
                            queryParams.EntityType),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetBookmarksHandler
    : IQueryHandlerWithResult<CursorResponse<BookmarkedMaterialDto>, GetBookmarksQuery>
{
    private readonly IValidator<GetBookmarksQuery> _validator;
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;

    public GetBookmarksHandler(
        IValidator<GetBookmarksQuery> validator,
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData user)
    {
        _validator = validator;
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _entitlementChecker = entitlementChecker;
        _user = user;
    }

    public async Task<Result<CursorResponse<BookmarkedMaterialDto>, Error>> Handle(
        GetBookmarksQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        EntityType? entityTypeFilter = query.EntityType;
        Cursor? cursor = Cursor.Decode(query.Cursor);
        DbConnection connection = _transactionManager.GetDbConnection();
        string? targetType = entityTypeFilter?.ToString();

        string sql = cursor is null
            ? """
              SELECT
                  mb.id AS Id,
                  mb.course_id AS CourseId,
                  mb.target_entity_type AS TargetType,
                  mb.target_entity_id AS TargetId,
                  mb.created_at AS CreatedAt,
                  (
                      SELECT COUNT(*)
                      FROM material_bookmarks
                      WHERE user_id = @UserId
                        AND (@CourseId IS NULL OR course_id = @CourseId)
                        AND (@TargetType IS NULL OR target_entity_type = @TargetType)
                  ) AS TotalCount
              FROM material_bookmarks mb
              WHERE mb.user_id = @UserId
                AND (@CourseId IS NULL OR mb.course_id = @CourseId)
                AND (@TargetType IS NULL OR mb.target_entity_type = @TargetType)
              ORDER BY mb.created_at DESC, mb.id DESC
              LIMIT @Limit;
              """
            // Cursor-страницы НЕ пересчитывают COUNT(*) (#512) — Total нужен только
            // на первой странице: фронт (`entities/bookmark/api.ts` select) читает
            // totalCount исключительно из pages[0], последующие страницы отдают 0.
            : """
              SELECT
                  mb.id AS Id,
                  mb.course_id AS CourseId,
                  mb.target_entity_type AS TargetType,
                  mb.target_entity_id AS TargetId,
                  mb.created_at AS CreatedAt,
                  CAST(0 AS bigint) AS TotalCount
              FROM material_bookmarks mb
              WHERE mb.user_id = @UserId
                AND (@CourseId IS NULL OR mb.course_id = @CourseId)
                AND (@TargetType IS NULL OR mb.target_entity_type = @TargetType)
                AND (mb.created_at, mb.id) < (@CursorCreatedAt, @CursorId)
              ORDER BY mb.created_at DESC, mb.id DESC
              LIMIT @Limit;
              """;

        List<BookmarkRow> rows = (await connection.QueryAsync<BookmarkRow>(
            new CommandDefinition(
                sql,
                new
                {
                    UserId = _user.UserId,
                    CourseId = query.CourseId,
                    TargetType = targetType,
                    CursorCreatedAt = cursor?.EnrolledAt,
                    CursorId = cursor?.LastId,
                    Limit = query.Limit + 1
                },
                cancellationToken: cancellationToken))).ToList();

        long totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;
        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        if (rows.Count == 0)
        {
            return new CursorResponse<BookmarkedMaterialDto> { Items = [], NextCursor = null, TotalCount = totalCount };
        }

        Result<IReadOnlyCollection<ResolvedMaterialDto>, Error> resolveResult =
            await _educationContentServiceClient.ResolveMaterialTargetsAsync(
                new ResolveMaterialTargetsRequest(
                    rows.Select(x => new MaterialResolveRequestItem(
                            x.CourseId,
                            new EntityReferenceDto(x.TargetType, x.TargetId)))
                        .ToArray()),
                cancellationToken);

        if (resolveResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        Dictionary<ResolvedMaterialKey, ResolvedMaterialDto> resolvedLookup =
            resolveResult.Value.ToDictionary(
                x => new ResolvedMaterialKey(x.CourseId, x.Target.Type, x.Target.Id),
                x => x);

        // Batch entitlement check — separately for Material и Issue.
        // Если grant потерян (enrollment отозван), закладка остаётся видимой
        // с lock-иконкой, а не тихо исчезает.
        AccessSubject subject = _user.ToAccessSubject();
        List<Guid> materialIds = rows
            .Where(r => r.TargetType == EntityType.Material)
            .Select(r => r.TargetId)
            .Distinct()
            .ToList();
        List<Guid> issueIds = rows
            .Where(r => r.TargetType == EntityType.Issue)
            .Select(r => r.TargetId)
            .Distinct()
            .ToList();

        Task<IReadOnlyDictionary<Guid, AccessDecision>> materialAccessTask =
            materialIds.Count > 0
                ? _entitlementChecker.CheckAccessBatchAsync(
                    subject, ResourceTypes.MATERIAL, materialIds, cancellationToken)
                : Task.FromResult<IReadOnlyDictionary<Guid, AccessDecision>>(
                    new Dictionary<Guid, AccessDecision>());
        Task<IReadOnlyDictionary<Guid, AccessDecision>> issueAccessTask =
            issueIds.Count > 0
                ? _entitlementChecker.CheckAccessBatchAsync(
                    subject, ResourceTypes.ISSUE, issueIds, cancellationToken)
                : Task.FromResult<IReadOnlyDictionary<Guid, AccessDecision>>(
                    new Dictionary<Guid, AccessDecision>());

        IReadOnlyDictionary<Guid, AccessDecision> materialAccess = await materialAccessTask;
        IReadOnlyDictionary<Guid, AccessDecision> issueAccess = await issueAccessTask;

        List<BookmarkedMaterialDto> items = [];
        foreach (BookmarkRow row in rows)
        {
            if (!resolvedLookup.TryGetValue(new ResolvedMaterialKey(row.CourseId, row.TargetType, row.TargetId),
                    out ResolvedMaterialDto? resolved))
            {
                continue;
            }

            IReadOnlyDictionary<Guid, AccessDecision> accessMap =
                row.TargetType == EntityType.Material ? materialAccess : issueAccess;
            bool isAccessible = !accessMap.TryGetValue(row.TargetId, out AccessDecision? decision)
                || decision.IsGranted;
            // Пользователь всегда авторизован (эндпоинт под RequirePermissions),
            // поэтому lockReason не бывает "anonymous". Точную причину (trial_required
            // vs standard_required) без AccessType ресурса не определить — отдаём
            // коарс "not_enrolled", который корректно покрывает ре-enrollment сценарий.
            string? lockReason = isAccessible ? null : LockReasons.NOT_ENROLLED;

            items.Add(new BookmarkedMaterialDto(
                row.CourseId,
                resolved.CourseSlug,
                resolved.CourseTitle,
                new EntityReferenceDto(resolved.Target.Type, resolved.Target.Id),
                resolved.Title,
                resolved.SectionTitle,
                resolved.SectionType,
                row.CreatedAt,
                isAccessible,
                lockReason));
        }

        string? nextCursor = hasMore
            ? Cursor.Encode(rows[^1].CreatedAt, rows[^1].Id)
            : null;

        return new CursorResponse<BookmarkedMaterialDto>
        {
            Items = items, NextCursor = nextCursor, TotalCount = totalCount
        };
    }

    private sealed class BookmarkRow
    {
        public Guid Id { get; init; }
        public Guid CourseId { get; init; }
        public EntityType TargetType { get; init; }
        public Guid TargetId { get; init; }
        public DateTime CreatedAt { get; init; }
        public long TotalCount { get; init; }
    }

    private readonly record struct ResolvedMaterialKey(Guid CourseId, EntityType TargetType, Guid TargetId);
}
