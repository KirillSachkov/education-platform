namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchProjectsRequest(
    string? Cursor,
    int Limit);
