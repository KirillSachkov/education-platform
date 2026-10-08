using Common;
using EducationContentService.Core.Features.ContentAccess;

namespace EducationContentService.Core.Features.Search.Export;

internal sealed class SearchExportEntityRow
{
    public EntityType EntityType { get; init; }
    public Guid EntityId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid? ImageId { get; init; }
    public string[] AccessTypes { get; init; } = [];
    public Guid? CourseId { get; init; }
    public string? CourseSlug { get; init; }
    public string? CourseTitle { get; init; }
    public Guid? ProjectId { get; init; }
    public string? ProjectTitle { get; init; }
    public Guid? ModuleId { get; init; }
    public string? ModuleTitle { get; init; }
    public DateTime UpdatedAt { get; init; }
    public Guid? AuthorId { get; init; }
    public string? MaterialKind { get; init; }
    public string? Content { get; init; }
    public Guid? VideoId { get; init; }
    public string[]? ChapterTitles { get; init; }
    public int[]? ChapterTimestamps { get; init; }
    public bool IsCourseOrphaned { get; init; }

    public IReadOnlyList<string> BuildRequiredAccessTags()
    {
        if (AccessTypes.Length == 0)
        {
            return ContentAccessTagBuilder.BuildCoursePublicDefault();
        }

        // Emit platform/course plan-tag aliases so plan-grant holders see the resource
        // as accessible in Typesense's enrichment. AuthorId is only display metadata.
        return ContentAccessTagBuilder.BuildMany(
            AccessTypes,
            EntityId,
            CourseId.HasValue ? [CourseId.Value] : [],
            logger: null);
    }
}
