namespace EducationContentService.Contracts.SearchExport;

public sealed record ExportSearchMaterialsRequest(
    string? Cursor,
    int Limit);
