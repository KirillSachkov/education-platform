namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchCoursesRequest(
    string? Cursor,
    int Limit);
