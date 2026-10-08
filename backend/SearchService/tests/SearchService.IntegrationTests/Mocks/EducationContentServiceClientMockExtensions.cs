using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Common;
using ContentAccess;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Modules;
using EducationContentService.Contracts.Projects;
using EducationContentService.Contracts.SearchExport;
using EducationContentService.Contracts.SearchLookup;
using NSubstitute;
using SharedKernel;

namespace SearchService.IntegrationTests.Mocks;

public static class EducationContentServiceClientMockExtensions
{
    private static readonly ConditionalWeakTable<IEducationContentServiceClient, MockState> _states = [];

    public static readonly DateTime CourseCreatedAtUtc = new(2024, 1, 9, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime CourseUpdatedAtUtc = new(2024, 1, 9, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ModuleCreatedAtUtc = new(2024, 1, 10, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ModuleUpdatedAtUtc = new(2024, 1, 10, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ProjectCreatedAtUtc = new(2024, 1, 10, 14, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ProjectUpdatedAtUtc = new(2024, 1, 10, 16, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime MaterialCreatedAtUtc = new(2024, 1, 11, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime MaterialUpdatedAtUtc = new(2024, 1, 11, 12, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<string> MaterialUpdatedChapterTitles =
        ["Intro", "Architecture", "Q&A"];

    public static readonly IReadOnlyList<int> MaterialUpdatedChapterTimestamps =
        [0, 120, 300];
    public static readonly DateTime IssueCreatedAtUtc = new(2024, 1, 12, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime IssueUpdatedAtUtc = new(2024, 1, 12, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime CollectionUpdatedAtUtc = new(2024, 1, 13, 12, 0, 0, DateTimeKind.Utc);

    public static IEducationContentServiceClient CreateMock()
    {
        var state = new MockState();
        IEducationContentServiceClient mockClient = Substitute.For<IEducationContentServiceClient>();
        _states.Add(mockClient, state);

        mockClient.GetCourseDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid courseId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetCourseCallNumber(courseId) > 1;

                return Task.FromResult(Result.Success<CourseDetailDto, Error>(new CourseDetailDto(
                    courseId,
                    Guid.NewGuid(),
                    isPublishedOrUpdated ? "course-new" : "course-title",
                    isPublishedOrUpdated ? "Course new" : "Course title",
                    isPublishedOrUpdated ? "Course description new" : "Course description",
                    isPublishedOrUpdated ? "Published" : "Draft",
                    "COURSE",
                    null,
                    null,
                    CourseCreatedAtUtc,
                    isPublishedOrUpdated ? CourseUpdatedAtUtc : CourseCreatedAtUtc,
                    [])));
            });

        mockClient.GetModuleDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid moduleId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetModuleCallNumber(moduleId) > 1;
                Guid courseId = GetCourseIdForEntity(moduleId);

                return Task.FromResult(Result.Success<ModuleDetailDto, Error>(new ModuleDetailDto(
                    moduleId,
                    Guid.NewGuid(),
                    isPublishedOrUpdated ? "Module new" : "Module title",
                    isPublishedOrUpdated ? "Description new" : "Module description",
                    null,
                    courseId,
                    isPublishedOrUpdated ? "Published" : "Draft",
                    ModuleCreatedAtUtc,
                    isPublishedOrUpdated ? ModuleUpdatedAtUtc : ModuleCreatedAtUtc,
                    [
                        new ModuleItemDto(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "Material",
                            "0001",
                            false,
                            "Primary",
                            "Public material",
                            "Published",
                            "PUBLIC"),
                        new ModuleItemDto(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "Material",
                            "0002",
                            false,
                            "Primary",
                            "Paid material",
                            "Published",
                            "ENROLLED"),
                    ])));
            });

        mockClient.GetProjectDetailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid projectId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetProjectCallNumber(projectId) > 1;

                return Task.FromResult(Result.Success<ProjectDetailDto, Error>(new ProjectDetailDto(
                    projectId,
                    Guid.NewGuid(),
                    isPublishedOrUpdated ? "Project new" : "Project title",
                    isPublishedOrUpdated ? "Project description new" : "Project description",
                    null,
                    isPublishedOrUpdated ? "Published" : "Draft",
                    ProjectCreatedAtUtc,
                    isPublishedOrUpdated ? ProjectUpdatedAtUtc : ProjectCreatedAtUtc,
                    true,
                    true,
                    true,
                    [
                        new ProjectItemDto(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "0001",
                            false,
                            null,
                            "Free issue",
                            "Published",
                            "FREE",
                            "PULL_REQUEST",
                            null),
                        new ProjectItemDto(
                            Guid.NewGuid(),
                            Guid.NewGuid(),
                            "0002",
                            false,
                            null,
                            "Paid issue",
                            "Published",
                            "ENROLLED",
                            "PULL_REQUEST",
                            null),
                    ])));
            });

        mockClient.GetDetailIssueByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid issueId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetIssueCallNumber(issueId) > 1;
                Guid projectId = GetProjectIdForEntity(issueId);

                return Task.FromResult(Result.Success<IssueDetailDto, Error>(new IssueDetailDto(
                    issueId,
                    projectId,
                    isPublishedOrUpdated ? "Issue new" : "Issue title",
                    isPublishedOrUpdated ? "Content new" : "Issue content",
                    isPublishedOrUpdated ? "Published" : "Draft",
                    "ENROLLED",
                    isPublishedOrUpdated,
                    "PULL_REQUEST",
                    null,
                    true,
                    true,
                    true,
                    IssueCreatedAtUtc,
                    isPublishedOrUpdated ? IssueUpdatedAtUtc : IssueCreatedAtUtc,
                    [],
                    [])));
            });

        mockClient.GetCourseSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid courseId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetCourseCallNumber(courseId) > 1;

                return Task.FromResult(Result.Success<CourseSearchLookupDto, Error>(new CourseSearchLookupDto(
                    courseId,
                    isPublishedOrUpdated ? "course-new" : "course-slug",
                    isPublishedOrUpdated ? "Course new" : "Course title",
                    isPublishedOrUpdated ? "Course description new" : "Course description",
                    isPublishedOrUpdated ? PublicationStatus.PUBLISHED : PublicationStatus.DRAFT,
                    isPublishedOrUpdated ? CourseUpdatedAtUtc : CourseCreatedAtUtc,
                    [GrantTags.Course(courseId)])));
            });

        mockClient.GetModuleSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid moduleId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetModuleCallNumber(moduleId) > 1;
                Guid courseId = GetCourseIdForEntity(moduleId);

                return Task.FromResult(Result.Success<ModuleSearchLookupDto, Error>(new ModuleSearchLookupDto(
                    moduleId,
                    courseId,
                    "course-slug",
                    isPublishedOrUpdated ? "Module new" : "Module title",
                    isPublishedOrUpdated ? "Description new" : "Module description",
                    "Course title",
                    isPublishedOrUpdated ? PublicationStatus.PUBLISHED : PublicationStatus.DRAFT,
                    isPublishedOrUpdated ? ModuleUpdatedAtUtc : ModuleCreatedAtUtc,
                    [GrantTags.PUBLIC, GrantTags.Course(courseId)])));
            });

        mockClient.GetProjectSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid projectId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetProjectCallNumber(projectId) > 1;
                Guid courseId = GetCourseIdForEntity(projectId);

                return Task.FromResult(Result.Success<ProjectSearchLookupDto, Error>(new ProjectSearchLookupDto(
                    projectId,
                    courseId,
                    "course-slug",
                    isPublishedOrUpdated ? "Project new" : "Project title",
                    isPublishedOrUpdated ? "Project description new" : "Project description",
                    "Course title",
                    isPublishedOrUpdated ? PublicationStatus.PUBLISHED : PublicationStatus.DRAFT,
                    isPublishedOrUpdated ? ProjectUpdatedAtUtc : ProjectCreatedAtUtc,
                    [GrantTags.CourseTrial(courseId), GrantTags.Course(courseId)])));
            });

        mockClient.GetMaterialSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid materialId = callInfo.Arg<Guid>();

                // Seeded override wins (used by course-archive cascade tests #378 to
                // control IsCourseOrphaned). Иначе — синтетический DTO по call-count.
                if (state.TryGetMaterialLookup(materialId, out MaterialSearchLookupDto? seeded))
                {
                    return Task.FromResult(Result.Success<MaterialSearchLookupDto, Error>(seeded!));
                }

                bool isPublishedOrUpdated = state.GetMaterialCallNumber(materialId) > 1;
                Guid moduleId = GetModuleIdForEntity(materialId);
                Guid courseId = GetCourseIdForEntity(materialId);

                return Task.FromResult(Result.Success<MaterialSearchLookupDto, Error>(new MaterialSearchLookupDto(
                    materialId,
                    courseId,
                    "course-slug",
                    isPublishedOrUpdated ? "Material new" : "Material title",
                    Guid.CreateVersion7(),
                    moduleId,
                    "Course title",
                    "Module title",
                    isPublishedOrUpdated ? PublicationStatus.PUBLISHED : PublicationStatus.DRAFT,
                    [GrantTags.Course(courseId)],
                    isPublishedOrUpdated ? MaterialUpdatedAtUtc : MaterialCreatedAtUtc,
                    MaterialKind: "VIDEO",
                    ChapterTitles: isPublishedOrUpdated ? MaterialUpdatedChapterTitles : null,
                    ChapterTimestamps: isPublishedOrUpdated ? MaterialUpdatedChapterTimestamps : null)));
            });

        mockClient.GetIssueSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid issueId = callInfo.Arg<Guid>();
                bool isPublishedOrUpdated = state.GetIssueCallNumber(issueId) > 1;
                Guid projectId = GetProjectIdForEntity(issueId);
                Guid moduleId = GetModuleIdForEntity(issueId);
                Guid courseId = GetCourseIdForEntity(issueId);

                return Task.FromResult(Result.Success<IssueSearchLookupDto, Error>(new IssueSearchLookupDto(
                    issueId,
                    projectId,
                    courseId,
                    "course-slug",
                    isPublishedOrUpdated ? "Issue new" : "Issue title",
                    moduleId,
                    "Course title",
                    "Project title",
                    "Module title",
                    isPublishedOrUpdated ? PublicationStatus.PUBLISHED : PublicationStatus.DRAFT,
                    [GrantTags.Course(courseId)],
                    isPublishedOrUpdated ? IssueUpdatedAtUtc : IssueCreatedAtUtc)));
            });

        mockClient.GetCollectionSearchLookupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Guid collectionId = callInfo.Arg<Guid>();

                CollectionSearchLookupDto dto = state.TryGetCollectionLookup(collectionId, out CollectionSearchLookupDto? seeded)
                    ? seeded!
                    : new CollectionSearchLookupDto(
                        Id: collectionId,
                        CourseId: null,
                        CourseSlug: null,
                        Title: "Collection title",
                        Description: "Collection description",
                        ImageId: null,
                        CourseTitle: null,
                        Status: PublicationStatus.PUBLISHED,
                        RequiredAccessTags: [GrantTags.PUBLIC],
                        UpdatedAt: CollectionUpdatedAtUtc);

                return Task.FromResult(Result.Success<CollectionSearchLookupDto, Error>(dto));
            });

        // Course → material ids (course-archive cascade #378). Default: empty (no children)
        // so existing course soft-delete/restore tests don't fan out; seed per-test.
        mockClient.GetCourseMaterialIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CourseMaterialIdsDto, Error>(
                new CourseMaterialIdsDto(state.GetCourseMaterialIds(callInfo.Arg<Guid>())))));

        mockClient.ExportAllSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    null,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        mockClient.ExportCourseSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    EntityType.Course,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        mockClient.ExportModuleSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    EntityType.Module,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        mockClient.ExportProjectSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    EntityType.Project,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        mockClient.ExportMaterialSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    EntityType.Material,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        mockClient.ExportIssueSearchEntitiesAsync(
                Arg.Any<string?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(Result.Success<CursorResponse<SearchExportEntityDto>, Error>(
                state.GetSearchExportBatch(
                    EntityType.Issue,
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<int>(1)))));

        return mockClient;
    }

    public static void ConfigureExportDocuments<TExport>(
        this IEducationContentServiceClient client,
        EntityType entityType,
        IReadOnlyList<TExport> documents)
        where TExport : class
    {
        MockState state = _states.GetOrCreateValue(client);
        state.SetExportDocuments(entityType, documents);
    }

    public static void SeedCollectionLookup(
        this IEducationContentServiceClient client,
        CollectionSearchLookupDto dto)
    {
        MockState state = _states.GetOrCreateValue(client);
        state.StoreCollectionLookup(dto);
    }

    public static void SeedCourseMaterialIds(
        this IEducationContentServiceClient client,
        Guid courseId,
        IReadOnlyList<Guid> materialIds)
    {
        MockState state = _states.GetOrCreateValue(client);
        state.StoreCourseMaterialIds(courseId, materialIds);
    }

    public static void SeedMaterialLookup(
        this IEducationContentServiceClient client,
        MaterialSearchLookupDto dto)
    {
        MockState state = _states.GetOrCreateValue(client);
        state.StoreMaterialLookup(dto);
    }

    public static Guid GetCourseIdForEntity(Guid entityId) => MutateGuid(entityId, 0x11, 0x22, 0x33);

    public static Guid GetProjectIdForEntity(Guid entityId) => MutateGuid(entityId, 0x44, 0x55, 0x66);

    public static Guid GetModuleIdForEntity(Guid entityId) => MutateGuid(entityId, 0x5A, 0xA5, 0x3C);

    private static Guid MutateGuid(Guid entityId, byte first, byte second, byte third)
    {
        byte[] bytes = entityId.ToByteArray();
        bytes[0] ^= first;
        bytes[5] ^= second;
        bytes[10] ^= third;

        return new Guid(bytes);
    }

    private sealed class MockState
    {
        private readonly ConcurrentDictionary<Guid, int> _courseCallNumbers = new();
        private readonly ConcurrentDictionary<Guid, int> _moduleCallNumbers = new();
        private readonly ConcurrentDictionary<Guid, int> _projectCallNumbers = new();
        private readonly ConcurrentDictionary<Guid, int> _materialCallNumbers = new();
        private readonly ConcurrentDictionary<Guid, int> _issueCallNumbers = new();
        private readonly ConcurrentDictionary<EntityType, IReadOnlyList<object>> _exportDocuments = new();
        private readonly ConcurrentDictionary<Guid, CollectionSearchLookupDto> _collectionLookups = new();
        private readonly ConcurrentDictionary<Guid, IReadOnlyList<Guid>> _courseMaterialIds = new();
        private readonly ConcurrentDictionary<Guid, MaterialSearchLookupDto> _materialLookups = new();

        public int GetCourseCallNumber(Guid courseId) =>
            _courseCallNumbers.AddOrUpdate(courseId, 1, (_, current) => current + 1);

        public int GetModuleCallNumber(Guid moduleId) =>
            _moduleCallNumbers.AddOrUpdate(moduleId, 1, (_, current) => current + 1);

        public int GetProjectCallNumber(Guid projectId) =>
            _projectCallNumbers.AddOrUpdate(projectId, 1, (_, current) => current + 1);

        public int GetMaterialCallNumber(Guid materialId) =>
            _materialCallNumbers.AddOrUpdate(materialId, 1, (_, current) => current + 1);

        public int GetIssueCallNumber(Guid issueId) =>
            _issueCallNumbers.AddOrUpdate(issueId, 1, (_, current) => current + 1);

        public void StoreCollectionLookup(CollectionSearchLookupDto dto) =>
            _collectionLookups[dto.Id] = dto;

        public bool TryGetCollectionLookup(Guid collectionId, out CollectionSearchLookupDto? dto) =>
            _collectionLookups.TryGetValue(collectionId, out dto);

        public void StoreCourseMaterialIds(Guid courseId, IReadOnlyList<Guid> materialIds) =>
            _courseMaterialIds[courseId] = materialIds;

        public IReadOnlyList<Guid> GetCourseMaterialIds(Guid courseId) =>
            _courseMaterialIds.TryGetValue(courseId, out IReadOnlyList<Guid>? ids) ? ids : Array.Empty<Guid>();

        public void StoreMaterialLookup(MaterialSearchLookupDto dto) =>
            _materialLookups[dto.Id] = dto;

        public bool TryGetMaterialLookup(Guid materialId, out MaterialSearchLookupDto? dto) =>
            _materialLookups.TryGetValue(materialId, out dto);

        public void SetExportDocuments<TExport>(EntityType entityType, IReadOnlyList<TExport> documents)
            where TExport : class
        {
            object[] normalizedDocuments = documents
                .OrderBy(GetEntityId)
                .Cast<object>()
                .ToArray();

            _exportDocuments[entityType] = normalizedDocuments;
        }

        public CursorResponse<SearchExportEntityDto> GetSearchExportBatch(
            EntityType? entityType,
            string? cursor,
            int limit)
        {
            IReadOnlyList<SearchExportEntityDto> sourceDocuments = GetSourceDocuments(entityType);
            (int EntityTypeOrder, Guid EntityId)? parsedCursor = ParseCursor(cursor);

            SearchExportEntityDto[] page = sourceDocuments
                .Where(item => parsedCursor is null || CompareCursor(item, parsedCursor.Value) > 0)
                .OrderBy(GetEntityTypeOrder)
                .ThenBy(static item => item.EntityId)
                .Take(limit + 1)
                .ToArray();

            SearchExportEntityDto[] items = page
                .Take(limit)
                .ToArray();

            SearchExportEntityDto? nextCursorItem = page.Length > limit ? items[^1] : null;

            return new CursorResponse<SearchExportEntityDto>
            {
                Items = items,
                NextCursor = nextCursorItem is null
                    ? null
                    : $"{GetEntityTypeOrder(nextCursorItem)}:{nextCursorItem.EntityId:D}",
                TotalCount = sourceDocuments.Count,
            };
        }

        private IReadOnlyList<SearchExportEntityDto> GetSourceDocuments(EntityType? entityType)
        {
            if (entityType.HasValue)
            {
                return _exportDocuments.TryGetValue(entityType.Value, out IReadOnlyList<object>? documents)
                    ? documents.OfType<SearchExportEntityDto>().ToArray()
                    : [];
            }

            return _exportDocuments.Values
                .SelectMany(static documents => documents)
                .OfType<SearchExportEntityDto>()
                .ToArray();
        }

        private static Guid GetEntityId(object exportDocument) =>
            exportDocument switch
            {
                SearchExportEntityDto dto => dto.EntityId,
                _ => throw new ArgumentOutOfRangeException(nameof(exportDocument), exportDocument.GetType(), null),
            };

        private static (int EntityTypeOrder, Guid EntityId)? ParseCursor(string? cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return null;
            }

            string[] parts = cursor.Split(':', 2);
            return parts.Length == 2 &&
                   int.TryParse(parts[0], out int entityTypeOrder) &&
                   Guid.TryParse(parts[1], out Guid entityId)
                ? (entityTypeOrder, entityId)
                : null;
        }

        private static int CompareCursor(
            SearchExportEntityDto item,
            (int EntityTypeOrder, Guid EntityId) cursor)
        {
            int itemEntityTypeOrder = GetEntityTypeOrder(item);
            int typeComparison = itemEntityTypeOrder.CompareTo(cursor.EntityTypeOrder);
            return typeComparison != 0
                ? typeComparison
                : item.EntityId.CompareTo(cursor.EntityId);
        }

        private static int GetEntityTypeOrder(SearchExportEntityDto dto) =>
            dto.EntityType switch
            {
                EntityType.Course => 1,
                EntityType.Module => 2,
                EntityType.Project => 3,
                EntityType.Material => 4,
                EntityType.Issue => 5,
                EntityType.Collection => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(dto), dto.EntityType, null),
            };

    }
}
