using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Domain;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using FluentValidation;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.Queries;

/// <param name="Kind">
///     Опциональный фильтр по типу курса (<c>COURSE</c> / <c>INTENSIVE</c>). Регистр игнорируется.
///     Если не задан — возвращаются курсы обоих типов.
/// </param>
public sealed record GetMyCoursesQuery(string? Cursor, int Limit, string? Kind) : IQuery;

public sealed class GetMyCoursesQueryValidator : AbstractValidator<GetMyCoursesQuery>
{
    public GetMyCoursesQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);

        When(x => !string.IsNullOrWhiteSpace(x.Kind), () =>
            RuleFor(x => x.Kind!)
                .Must(k => Enum.TryParse<Domain.Courses.CourseKind>(k, ignoreCase: true, out _))
                .WithError(GeneralErrors.ValueIsInvalid(nameof(GetMyCoursesQuery.Kind))));
    }
}

public sealed class GetMyCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/my", async Task<EndpointResult<CursorResponse<CourseSummaryDto>>> (
                    [AsParameters] GetMyCoursesQuery request,
                    [FromServices] GetMyCoursesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(request, cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class GetMyCoursesHandler : IQueryHandler<CursorResponse<CourseSummaryDto>, GetMyCoursesQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ILogger<GetMyCoursesHandler> _logger;

    public GetMyCoursesHandler(
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        IAuthorLookupClient authorLookupClient,
        IFileServiceClient fileServiceClient,
        ILogger<GetMyCoursesHandler> logger)
    {
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _authorLookupClient = authorLookupClient;
        _fileServiceClient = fileServiceClient;
        _logger = logger;
    }

    public async Task<CursorResponse<CourseSummaryDto>> Handle(
        GetMyCoursesQuery query, CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        SortKeyCursor? cursor = SortKeyCursor.Decode(query.Cursor);

        const string sql = """
                           SELECT
                               id, author_id, slug, title, description, status, kind, image_id, video_id, is_new,
                               show_in_full_access, is_catalog_listed, sort_key, created_at, updated_at,
                               COUNT(*) OVER() AS total_count
                           FROM courses
                           WHERE (@CanViewAll = TRUE OR author_id = @AuthorId)
                             AND (@Kind IS NULL OR kind = @Kind)
                             AND (@CursorSortKey IS NULL OR (sort_key, id) > (@CursorSortKey, @CursorId))
                           ORDER BY sort_key ASC, id ASC
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        long totalCount = 0;

        List<CourseSummaryRow> rows = (await connection.QueryAsync<CourseSummaryRow, long, CourseSummaryRow>(
            sql,
            param: new
            {
                AuthorId = _userScopedData.UserId,
                CanViewAll = _userScopedData.IsAdmin
                    || _userScopedData.HasPermission(PlatformPermissions.Content.MODERATE),
                Kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.ToUpperInvariant(),
                CursorSortKey = cursor?.SortKey,
                CursorId = cursor?.LastId,
                Limit = limit + 1
            },
            splitOn: "total_count",
            map: (row, tc) =>
            {
                totalCount = tc;
                return row;
            })).ToList();

        bool hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        // Author credit (#637): резолвим имя + аватар автора per distinct author, чтобы
        // platform-wide список «Курсы платформы» показывал нормального автора вместо GUID.
        // CachedAuthorLookupClient soft-degrade'ит до пустого словаря при недоступности AuthService.
        List<Guid> authorIds = rows.Select(r => r.AuthorId).Distinct().ToList();
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync(authorIds, cancellationToken);
        IReadOnlyDictionary<Guid, AuthorCreditDto> authorsMap = authorsResult.IsSuccess
            ? authorsResult.Value
            : new Dictionary<Guid, AuthorCreditDto>();

        // Один FileService-батч на все avatar-id (covers тут не нужны — это author-side список).
        List<Guid> avatarIds = authorsMap.Values
            .Where(a => a.AvatarId is not null)
            .Select(a => a.AvatarId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> avatarUrlMap = [];
        if (avatarIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(avatarIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        avatarUrlMap[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch batch avatars for my-courses list");
            }
        }

        List<CourseSummaryDto> courses = rows
            .Select(r =>
            {
                string? authorName = null;
                string? authorAvatarUrl = null;
                if (authorsMap.TryGetValue(r.AuthorId, out AuthorCreditDto? author))
                {
                    authorName = author.DisplayName;
                    if (author.AvatarId is { } avatarId
                        && avatarUrlMap.TryGetValue(avatarId, out string? avatarUrl))
                    {
                        authorAvatarUrl = avatarUrl;
                    }
                }

                return new CourseSummaryDto(
                    r.Id, r.AuthorId, r.Slug, r.Title, r.Description,
                    r.Status, r.Kind, r.ImageId, r.VideoId, r.IsNew,
                    r.SortKey,
                    r.CreatedAt, r.UpdatedAt,
                    ShowInFullAccess: r.ShowInFullAccess,
                    IsCatalogListed: r.IsCatalogListed,
                    AuthorDisplayName: authorName,
                    AuthorAvatarUrl: authorAvatarUrl);
            })
            .ToList();

        string? nextCursor = hasMore
            ? SortKeyCursor.Encode(rows[^1].SortKey, rows[^1].Id)
            : null;

        return new CursorResponse<CourseSummaryDto>
        {
            Items = courses, NextCursor = nextCursor, TotalCount = totalCount
        };
    }

    private sealed class CourseSummaryRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string Status { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public Guid? VideoId { get; init; }
        public bool IsNew { get; init; }
        public bool ShowInFullAccess { get; init; }
        public bool IsCatalogListed { get; init; }
        public string SortKey { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
