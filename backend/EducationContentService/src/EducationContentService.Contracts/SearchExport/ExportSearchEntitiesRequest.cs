namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchEntitiesRequest(
    string? Cursor,
    int Limit);
