namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Параметры запроса списка материалов с курсорной пагинацией.
/// </summary>
public sealed record GetMaterialsRequest(
    string? Scope = null,
    string? Cursor = null,
    int Limit = 20,
    string? Kind = null,
    Guid? AuthorId = null,
    string? Search = null);
