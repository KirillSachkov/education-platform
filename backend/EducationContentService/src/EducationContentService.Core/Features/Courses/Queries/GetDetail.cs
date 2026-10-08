using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Domain;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record GetCourseDetailQuery(Guid CourseId) : IQuery;

public sealed class GetCourseDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/detail", async Task<EndpointResult<CourseDetailDto>> (
                [FromRoute] Guid courseId,
                [FromServices] GetCourseDetailHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new GetCourseDetailQuery(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class GetCourseDetailHandler : IQueryHandlerWithResult<CourseDetailDto, GetCourseDetailQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly UserScopedData _userScopedData;

    public GetCourseDetailHandler(
        ITransactionManager transactionManager,
        IAuthorLookupClient authorLookupClient,
        IFileServiceClient fileServiceClient,
        UserScopedData userScopedData)
    {
        _transactionManager = transactionManager;
        _authorLookupClient = authorLookupClient;
        _fileServiceClient = fileServiceClient;
        _userScopedData = userScopedData;
    }

    public async Task<Result<CourseDetailDto, Error>> Handle(
        GetCourseDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               c.id,
                               c.author_id,
                               c.slug,
                               c.title,
                               c.description,
                               c.status,
                               c.kind,
                               c.image_id,
                               c.video_id,
                               c.created_at,
                               c.updated_at
                           FROM courses c
                           WHERE c.id = @CourseId;

                           SELECT
                               ci.id,
                               ci.reference_id,
                               ci.item_type,
                               ci.sort_key,
                               ci.is_optional,
                               COALESCE(m.title, p.title) AS title,
                               COALESCE(m.status, p.status) AS status
                           FROM course_items ci
                           LEFT JOIN modules m ON ci.item_type = 'Module' AND ci.reference_id = m.id
                           LEFT JOIN projects p ON ci.item_type = 'Project' AND ci.reference_id = p.id
                           WHERE ci.course_id = @CourseId
                           ORDER BY ci.sort_key;
                           """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(sql, new { query.CourseId });

        CourseDetailRow? courseRow = await multi.ReadFirstOrDefaultAsync<CourseDetailRow>();
        if (courseRow == null)
            return GeneralErrors.NotFound(query.CourseId);

        // Author-scoped manage view — only the course owner (or admin) sees the editor DTO,
        // including draft items. Public consumers use GetCurriculum / GetCourseLanding instead.
        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseRow.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        IEnumerable<CourseItemRow> itemRows = await multi.ReadAsync<CourseItemRow>();

        List<CourseItemDto> items = itemRows.Select(i => new CourseItemDto(
            i.Id, i.ReferenceId, i.ItemType, i.SortKey, i.IsOptional, i.Title, i.Status)).ToList();

        (string? authorName, string? authorAvatarUrl) =
            await ResolveAuthorCreditAsync(courseRow.AuthorId, cancellationToken);

        return new CourseDetailDto(
            courseRow.Id,
            courseRow.AuthorId,
            courseRow.Slug,
            courseRow.Title,
            courseRow.Description,
            courseRow.Status,
            courseRow.Kind,
            courseRow.ImageId,
            courseRow.VideoId,
            courseRow.CreatedAt,
            courseRow.UpdatedAt,
            items,
            AuthorDisplayName: authorName,
            AuthorAvatarUrl: authorAvatarUrl);
    }

    // Resolves the author's display name + avatar URL (#569). Both best-effort: a degraded
    // AuthService / FileService leaves the credit null and never fails the detail response.
    private async Task<(string? DisplayName, string? AvatarUrl)> ResolveAuthorCreditAsync(
        Guid authorId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync([authorId], cancellationToken);

        if (authorsResult.IsFailure
            || !authorsResult.Value.TryGetValue(authorId, out AuthorCreditDto? author))
        {
            return (null, null);
        }

        string? avatarUrl = null;
        if (author.AvatarId is { } avatarId)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(avatarId, cancellationToken);
            if (fileResult is { IsSuccess: true, Value: not null })
                avatarUrl = fileResult.Value.ContentUrl;
        }

        return (author.DisplayName, avatarUrl);
    }

    private sealed class CourseDetailRow
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
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class CourseItemRow
    {
        public Guid Id { get; init; }
        public Guid ReferenceId { get; init; }
        public string ItemType { get; init; } = null!;
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string? Title { get; init; }
        public string? Status { get; init; }
    }
}
