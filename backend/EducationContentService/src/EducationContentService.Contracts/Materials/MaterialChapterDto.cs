namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Глава видео-материала. Заголовок + offset в секундах от начала видео.
///     Денормализуется из <c>Material.ChapterTitles[]</c> + <c>ChapterTimestamps[]</c>
///     для рендера student-facing списка глав под плеером.
/// </summary>
public sealed record MaterialChapterDto(string Title, int TimeSeconds);
