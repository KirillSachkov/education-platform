namespace ProgressService.Domain.Notes;

/// <summary>
///     Личная заметка пользователя к материалу. User-scoped: одна строка на пару
///     (UserId, MaterialId), upsert через PUT. Видна только владельцу — Tier-3
///     entitlement-чек не нужен (контент материала через заметку не возвращается).
///     Issue #465.
/// </summary>
public sealed class MaterialNote
{
    public const int MAX_CONTENT_LENGTH = 20000;

    private MaterialNote(Guid userId, Guid materialId, string content)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        MaterialId = materialId;
        Content = content;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    private MaterialNote()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MaterialId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<MaterialNote, Error> Create(Guid userId, Guid materialId, string content)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (materialId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(materialId));
        }

        UnitResult<Error> contentValidation = ValidateContent(content);
        if (contentValidation.IsFailure)
        {
            return contentValidation.Error;
        }

        return new MaterialNote(userId, materialId, content);
    }

    /// <summary>Обновляет текст заметки (update-ветка PUT-upsert'а).</summary>
    public UnitResult<Error> UpdateContent(string content)
    {
        UnitResult<Error> contentValidation = ValidateContent(content);
        if (contentValidation.IsFailure)
        {
            return contentValidation.Error;
        }

        Content = content;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    private static UnitResult<Error> ValidateContent(string content)
    {
        if (content is null)
        {
            return GeneralErrors.ValueIsRequired(nameof(content));
        }

        if (content.Length > MAX_CONTENT_LENGTH)
        {
            return ProgressErrors.MaterialNoteContentTooLong(MAX_CONTENT_LENGTH);
        }

        return UnitResult.Success<Error>();
    }
}
