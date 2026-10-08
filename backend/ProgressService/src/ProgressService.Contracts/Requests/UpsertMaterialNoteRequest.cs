namespace ProgressService.Contracts.Requests;

/// <summary>
///     Тело PUT /progress/materials/{materialId}/note — upsert личной заметки
///     текущего пользователя к материалу. Issue #465.
/// </summary>
public sealed record UpsertMaterialNoteRequest(string Content);
