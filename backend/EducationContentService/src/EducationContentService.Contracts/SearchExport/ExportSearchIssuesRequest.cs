namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchIssuesRequest(
    string? Cursor,
    int Limit);
