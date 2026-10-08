namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchModulesRequest(
    string? Cursor,
    int Limit);
