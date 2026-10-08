using ContentAccess;
using EducationContentService.Core.Features.ContentAccess;
using EducationContentService.Domain;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     Переписывает Redis-теги доступа для всех опубликованных материалов, заданий,
///     подборок, курсов и квизов.
///     Идемпотентна — можно вызывать повторно. Вызывается из <see cref="ResyncAccessTagsCli"/>
///     для аварийного восстановления Redis. В нормальной работе вся синхронизация
///     идёт через runtime event-handlers (<c>SyncMaterialAccessToRedisHandler</c> и др.).
///
///     Архитектура:
///     - Типы ресурсов (Materials / Issues / Collections / Courses / Quizzes) идут
///       параллельно, каждый в своём <see cref="AsyncServiceScope"/> с отдельным
///       <c>EducationDbContext</c>.
///     - Внутри каждого типа — keyset-пагинация по Id: короткие запросы по
///       <see cref="ChunkSize"/> строк, соединение к БД не держится всё время.
///     - Redis-записи отправляются батчами через pipeline
///       (<see cref="IResourceAccessWriter.SetTagsManyAsync"/>).
///     - Memory footprint: O(ChunkSize) на тип, не O(total rows).
/// </summary>
public sealed class AccessTagsResyncer
{
    private const int ChunkSize = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IResourceAccessWriter _writer;
    private readonly ILogger<AccessTagsResyncer> _logger;

    public AccessTagsResyncer(
        IServiceScopeFactory scopeFactory,
        IResourceAccessWriter writer,
        ILogger<AccessTagsResyncer> logger)
    {
        _scopeFactory = scopeFactory;
        _writer = writer;
        _logger = logger;
    }

    public async Task<ResyncStats> ResyncAllAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting resync of access tags for all published materials, issues, collections, courses and quizzes...");

        Task<int> materialsTask = RunInScopeAsync(ResyncMaterialsAsync, ct);
        Task<int> issuesTask = RunInScopeAsync(ResyncIssuesAsync, ct);
        Task<int> collectionsTask = RunInScopeAsync(ResyncCollectionsAsync, ct);
        Task<int> coursesTask = RunInScopeAsync(ResyncCoursesAsync, ct);
        Task<int> quizzesTask = RunInScopeAsync(ResyncQuizzesAsync, ct);

        await Task.WhenAll(materialsTask, issuesTask, collectionsTask, coursesTask, quizzesTask);

        ResyncStats stats = new(
            await materialsTask, await issuesTask, await collectionsTask, await coursesTask, await quizzesTask);
        _logger.LogInformation(
            "Access tags resync complete. Materials: {Materials}, Issues: {Issues}, Collections: {Collections}, Courses: {Courses}, Quizzes: {Quizzes}",
            stats.Materials, stats.Issues, stats.Collections, stats.Courses, stats.Quizzes);

        return stats;
    }

    private async Task<int> RunInScopeAsync(
        Func<EducationDbContext, CancellationToken, Task<int>> action,
        CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        EducationDbContext dbContext = scope.ServiceProvider.GetRequiredService<EducationDbContext>();
        return await action(dbContext, ct);
    }

    private Task<int> ResyncCollectionsAsync(EducationDbContext dbContext, CancellationToken ct) =>
        StreamAndWriteAsync(
            fetchChunk: async (lastId, take, token) =>
            {
                var rows = await (
                    from collection in dbContext.Collections.AsNoTracking()
                    where collection.Status == PublicationStatus.PUBLISHED && collection.Id > lastId
                    orderby collection.Id
                    select new { collection.Id, collection.AccessType, collection.CourseId, collection.AuthorId })
                    .Take(take)
                    .ToListAsync(token);

                return rows.ConvertAll(r => new Row(
                    r.Id,
                    r.AccessType,
                    r.CourseId == null ? Array.Empty<Guid>() : new[] { r.CourseId.Value },
                    r.AuthorId));
            },
            resourceType: ResourceTypes.COLLECTION,
            label: "Collections",
            ct);

    /// <summary>
    ///     Курсовой resource-тег (<c>resource-access:course:{id}</c>) — enrolled-гейт
    ///     для приватных материалов в программе и комментирования course-bound контента.
    ///     В отличие от material/issue/collection не зависит от AccessType: всегда
    ///     <c>[course:{id}, plan:all, plan:course:{id}]</c>
    ///     (см. <see cref="ContentAccessTagBuilder.BuildCourseAccessTags"/>).
    ///     Свой keyset-loop, а не <see cref="StreamAndWriteAsync"/>: курсу не нужна
    ///     per-chunk резолюция course→author, а tag-set
    ///     не зависит от AccessType — общий helper здесь не подходит.
    /// </summary>
    private async Task<int> ResyncCoursesAsync(EducationDbContext dbContext, CancellationToken ct)
    {
        int total = 0;
        Guid lastId = Guid.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var rows = await (
                from course in dbContext.Courses.AsNoTracking()
                where course.Status == PublicationStatus.PUBLISHED && course.Id > lastId
                orderby course.Id
                select new { course.Id })
                .Take(ChunkSize)
                .ToListAsync(ct);

            if (rows.Count == 0)
                break;

            List<ResourceAccessWrite> writes = rows.ConvertAll(r => new ResourceAccessWrite(
                ResourceTypes.COURSE,
                r.Id,
                ContentAccessTagBuilder.BuildCourseAccessTags(r.Id)));

            await _writer.SetTagsManyAsync(writes, ct);
            total += writes.Count;
            lastId = rows[^1].Id;

            _logger.LogInformation("Courses progress: {Total}", total);
        }

        _logger.LogInformation("Courses resynced: {Count}", total);
        return total;
    }

    private Task<int> ResyncMaterialsAsync(EducationDbContext dbContext, CancellationToken ct) =>
        StreamAndWriteAsync(
            fetchChunk: async (lastId, take, token) =>
            {
                var headers = await (
                    from material in dbContext.Materials.AsNoTracking()
                    where material.Status == PublicationStatus.PUBLISHED && material.Id > lastId
                    orderby material.Id
                    select new { material.Id, material.AccessType, material.AuthorId })
                    .Take(take)
                    .ToListAsync(token);

                if (headers.Count == 0)
                    return [];

                Guid[] ids = headers.ConvertAll(h => h.Id).ToArray();

                // course_materials — единственный источник правды о привязке материала к курсу (INV-4).
                var bindings = await dbContext.CourseMaterials.AsNoTracking()
                    .Where(cm => ids.Contains(cm.MaterialId))
                    .Select(cm => new { cm.MaterialId, cm.CourseId })
                    .ToListAsync(token);

                Dictionary<Guid, List<Guid>> courseMap = headers.ToDictionary(h => h.Id, _ => new List<Guid>());
                foreach (var b in bindings)
                    courseMap[b.MaterialId].Add(b.CourseId);

                return headers.ConvertAll(h => new Row(
                    h.Id,
                    h.AccessType,
                    courseMap[h.Id].Distinct().ToArray(),
                    h.AuthorId));
            },
            resourceType: ResourceTypes.MATERIAL,
            label: "Materials",
            ct);

    /// <summary>
    ///     Квизы (#490): теги только у PUBLISHED (как и runtime-путь — первый tag-set
    ///     делает quiz.published, у DRAFT тегов нет). CourseIds — из <c>course_quizzes</c>,
    ///     AuthorId — собственное поле квиза.
    /// </summary>
    private Task<int> ResyncQuizzesAsync(EducationDbContext dbContext, CancellationToken ct) =>
        StreamAndWriteAsync(
            fetchChunk: async (lastId, take, token) =>
            {
                var headers = await (
                    from quiz in dbContext.Quizzes.AsNoTracking()
                    where quiz.Status == PublicationStatus.PUBLISHED && quiz.Id > lastId
                    orderby quiz.Id
                    select new { quiz.Id, quiz.AccessType, quiz.AuthorId })
                    .Take(take)
                    .ToListAsync(token);

                if (headers.Count == 0)
                    return [];

                Guid[] ids = headers.ConvertAll(h => h.Id).ToArray();

                var bindings = await dbContext.CourseQuizzes.AsNoTracking()
                    .Where(cq => ids.Contains(cq.QuizId))
                    .Select(cq => new { cq.QuizId, cq.CourseId })
                    .ToListAsync(token);

                Dictionary<Guid, List<Guid>> courseMap = headers.ToDictionary(h => h.Id, _ => new List<Guid>());
                foreach (var b in bindings)
                    courseMap[b.QuizId].Add(b.CourseId);

                return headers.ConvertAll(h => new Row(
                    h.Id,
                    h.AccessType,
                    courseMap[h.Id].Distinct().ToArray(),
                    h.AuthorId));
            },
            resourceType: ResourceTypes.QUIZ,
            label: "Quizzes",
            ct);

    private Task<int> ResyncIssuesAsync(EducationDbContext dbContext, CancellationToken ct) =>
        StreamAndWriteAsync(
            fetchChunk: async (lastId, take, token) =>
            {
                var headers = await (
                    from issue in dbContext.Issues.AsNoTracking()
                    where issue.Status == PublicationStatus.PUBLISHED && issue.Id > lastId
                    orderby issue.Id
                    select new { issue.Id, issue.AccessType, issue.ProjectId })
                    .Take(take)
                    .ToListAsync(token);

                if (headers.Count == 0)
                    return [];

                Guid[] issueIds = headers.ConvertAll(h => h.Id).ToArray();
                Guid[] projectIds = headers.ConvertAll(h => h.ProjectId).Distinct().ToArray();

                // Issue → courseIds: UNION двух путей (см. IssuesRepository.GetCourseIdsAsync):
                //   1. project_id → course_items[item_type='Project']
                //   2. issue → module_items[item_type='Issue'] → course_items[item_type='Module']
                var moduleBindings = await (
                    from mi in dbContext.ModuleItems.AsNoTracking()
                    where mi.ItemType == Domain.Modules.ModuleItemType.Issue && issueIds.Contains(mi.ReferenceId)
                    join ci in dbContext.CourseItems.AsNoTracking()
                        on new { Ref = mi.ModuleId, T = Domain.Courses.CourseItemType.Module }
                        equals new { Ref = ci.ReferenceId, T = ci.ItemType }
                    select new { IssueId = mi.ReferenceId, ci.CourseId })
                    .ToListAsync(token);

                var projectBindings = await dbContext.CourseItems.AsNoTracking()
                    .Where(ci => ci.ItemType == Domain.Courses.CourseItemType.Project && projectIds.Contains(ci.ReferenceId))
                    .Select(ci => new { ProjectId = ci.ReferenceId, ci.CourseId })
                    .ToListAsync(token);

                Dictionary<Guid, List<Guid>> projectToCourses = projectBindings
                    .GroupBy(b => b.ProjectId)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.CourseId).ToList());

                // Issue → authorId: тащим через project (single source of truth по принадлежности задания).
                Dictionary<Guid, Guid> projectAuthors = await dbContext.Projects.AsNoTracking()
                    .Where(p => projectIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.AuthorId })
                    .ToDictionaryAsync(x => x.Id, x => x.AuthorId, token);

                Dictionary<Guid, List<Guid>> courseMap = headers.ToDictionary(h => h.Id, _ => new List<Guid>());
                foreach (var b in moduleBindings)
                    courseMap[b.IssueId].Add(b.CourseId);
                foreach (var h in headers)
                    if (projectToCourses.TryGetValue(h.ProjectId, out List<Guid>? courses))
                        courseMap[h.Id].AddRange(courses);

                return headers.ConvertAll(h => new Row(
                    h.Id,
                    h.AccessType,
                    courseMap[h.Id].Distinct().ToArray(),
                    projectAuthors.TryGetValue(h.ProjectId, out Guid authorId) ? authorId : null));
            },
            resourceType: ResourceTypes.ISSUE,
            label: "Issues",
            ct);

    private async Task<int> StreamAndWriteAsync(
        Func<Guid, int, CancellationToken, Task<List<Row>>> fetchChunk,
        string resourceType,
        string label,
        CancellationToken ct)
    {
        int total = 0;
        Guid lastId = Guid.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            List<Row> chunk = await fetchChunk(lastId, ChunkSize, ct);
            if (chunk.Count == 0)
                break;

            List<ResourceAccessWrite> writes = new(chunk.Count);
            foreach (Row row in chunk)
            {
                IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
                    row.AccessType, row.Id, row.CourseIds, _logger);
                writes.Add(new ResourceAccessWrite(resourceType, row.Id, tags));
            }

            await _writer.SetTagsManyAsync(writes, ct);
            total += writes.Count;
            lastId = chunk[^1].Id;

            _logger.LogInformation("{Label} progress: {Total}", label, total);
        }

        _logger.LogInformation("{Label} resynced: {Count}", label, total);
        return total;
    }

    private sealed record Row(Guid Id, AccessType AccessType, IReadOnlyList<Guid> CourseIds, Guid? AuthorId);
}

public readonly record struct ResyncStats(int Materials, int Issues, int Collections, int Courses, int Quizzes);
