using Common;
using EducationContentService.Contracts.SearchExport;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Domain;
using TagService.Contracts.SearchLookup;

namespace SearchService.Core.Features.EducationDocuments;

public static class EducationDocumentFactory
{
    public static EducationDocument FromCourse(CourseSearchLookupDto dto) =>
        EducationDocument.CreateCourse(
            dto.Id,
            dto.Title,
            dto.Description,
            dto.UpdatedAt,
            dto.RequiredAccessTags,
            null,
            isDeleted: dto.Status != PublicationStatus.PUBLISHED,
            courseSlug: dto.Slug,
            authorId: dto.AuthorId);

    public static EducationDocument FromModule(ModuleSearchLookupDto dto) =>
        EducationDocument.CreateModule(
            dto.Id,
            dto.Title,
            dto.Description,
            dto.UpdatedAt,
            dto.RequiredAccessTags,
            dto.CourseId,
            dto.CourseTitle,
            null,
            isDeleted: dto.Status != PublicationStatus.PUBLISHED,
            courseSlug: dto.CourseSlug,
            authorId: dto.AuthorId);

    public static EducationDocument FromProject(ProjectSearchLookupDto dto) =>
        EducationDocument.CreateProject(
            dto.Id,
            dto.Title,
            dto.Description,
            dto.UpdatedAt,
            dto.RequiredAccessTags,
            dto.CourseId,
            dto.CourseTitle,
            null,
            isDeleted: dto.Status != PublicationStatus.PUBLISHED,
            courseSlug: dto.CourseSlug,
            authorId: dto.AuthorId);

    public static EducationDocument FromMaterial(MaterialSearchLookupDto dto) =>
        EducationDocument.CreateMaterial(
            dto.Id,
            dto.Title,
            dto.UpdatedAt,
            dto.ImageId,
            dto.RequiredAccessTags,
            dto.CourseId,
            dto.CourseTitle,
            null,
            dto.ModuleId,
            dto.ModuleTitle,
            // Скрываем из поиска не только DRAFT/ARCHIVED-материалы, но и материалы,
            // осиротевшие от всех active-курсов (#378): course archive/delete оставляет
            // материал PUBLISHED, но он больше не должен находиться в выдаче.
            isDeleted: dto.Status != PublicationStatus.PUBLISHED || dto.IsCourseOrphaned,
            courseSlug: dto.CourseSlug,
            authorId: dto.AuthorId,
            materialKind: dto.MaterialKind,
            content: dto.Content,
            videoId: dto.VideoId,
            chapterTitles: dto.ChapterTitles,
            chapterTimestamps: dto.ChapterTimestamps);

    public static EducationDocument FromCollection(CollectionSearchLookupDto dto) =>
        EducationDocument.CreateCollection(
            dto.Id,
            dto.Title,
            dto.Description,
            dto.UpdatedAt,
            dto.ImageId,
            dto.RequiredAccessTags,
            dto.CourseId,
            dto.CourseTitle,
            null,
            isDeleted: dto.Status != PublicationStatus.PUBLISHED,
            courseSlug: dto.CourseSlug,
            authorId: dto.AuthorId);

    public static EducationDocument FromIssue(IssueSearchLookupDto dto) =>
        EducationDocument.CreateIssue(
            dto.Id,
            dto.Title,
            dto.UpdatedAt,
            dto.RequiredAccessTags,
            dto.CourseId,
            dto.CourseTitle,
            null,
            dto.ProjectId,
            dto.ProjectTitle,
            dto.ModuleId,
            dto.ModuleTitle,
            isDeleted: dto.Status != PublicationStatus.PUBLISHED,
            courseSlug: dto.CourseSlug,
            authorId: dto.AuthorId);

    public static Result<EducationDocument, Error> FromExport(
        SearchExportEntityDto dto,
        EntityTagsSearchLookupBatchDto? tags,
        string? reindexGeneration = null) =>
        dto.EntityType switch
        {
            EntityType.Course => Result.Success<EducationDocument, Error>(EducationDocument.CreateCourse(
                dto.EntityId,
                dto.Title,
                dto.Description,
                dto.UpdatedAt,
                dto.RequiredAccessTags,
                null,
                tags?.TagIds,
                tags?.TagTitles,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId)),

            EntityType.Module => Result.Success<EducationDocument, Error>(EducationDocument.CreateModule(
                dto.EntityId,
                dto.Title,
                dto.Description,
                dto.UpdatedAt,
                dto.RequiredAccessTags,
                dto.CourseId,
                dto.CourseTitle,
                null,
                tags?.TagIds,
                tags?.TagTitles,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId)),

            EntityType.Project => Result.Success<EducationDocument, Error>(EducationDocument.CreateProject(
                dto.EntityId,
                dto.Title,
                dto.Description,
                dto.UpdatedAt,
                dto.RequiredAccessTags,
                dto.CourseId,
                dto.CourseTitle,
                null,
                tags?.TagIds,
                tags?.TagTitles,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId)),

            EntityType.Material => Result.Success<EducationDocument, Error>(EducationDocument.CreateMaterial(
                dto.EntityId,
                dto.Title,
                dto.UpdatedAt,
                dto.ImageId,
                dto.RequiredAccessTags,
                dto.CourseId,
                dto.CourseTitle,
                null,
                dto.ModuleId,
                dto.ModuleTitle,
                tags?.TagIds,
                tags?.TagTitles,
                // Course-orphan материал (все курсы архивированы/удалены) скрыт из
                // выдачи и в full-reindex'е — иначе nightly rebuild вернёт его назад (#378).
                isDeleted: dto.IsCourseOrphaned,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId,
                materialKind: dto.MaterialKind,
                content: dto.Content,
                videoId: dto.VideoId,
                chapterTitles: dto.ChapterTitles,
                chapterTimestamps: dto.ChapterTimestamps)),

            EntityType.Issue => Result.Success<EducationDocument, Error>(EducationDocument.CreateIssue(
                dto.EntityId,
                dto.Title,
                dto.UpdatedAt,
                dto.RequiredAccessTags,
                dto.CourseId,
                dto.CourseTitle,
                null,
                dto.ProjectId,
                dto.ProjectTitle,
                dto.ModuleId,
                dto.ModuleTitle,
                tags?.TagIds,
                tags?.TagTitles,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId)),

            EntityType.Collection => Result.Success<EducationDocument, Error>(EducationDocument.CreateCollection(
                dto.EntityId,
                dto.Title,
                dto.Description,
                dto.UpdatedAt,
                dto.ImageId,
                dto.RequiredAccessTags,
                dto.CourseId,
                dto.CourseTitle,
                null,
                tags?.TagIds,
                tags?.TagTitles,
                reindexGeneration: reindexGeneration ?? EducationDocument.LIVE_REINDEX_GENERATION,
                courseSlug: dto.CourseSlug,
                authorId: dto.AuthorId)),

            _ => Result.Failure<EducationDocument, Error>(Error.Validation(
                "search.entity_type.unsupported",
                $"Неподдерживаемый тип сущности для поиска: {dto.EntityType}"))
        };
}
