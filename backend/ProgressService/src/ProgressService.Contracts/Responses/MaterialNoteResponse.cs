namespace ProgressService.Contracts.Responses;

/// <summary>
///     Личная заметка пользователя к материалу. Issue #465.
/// </summary>
public sealed record MaterialNoteResponse(
    Guid MaterialId,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);
