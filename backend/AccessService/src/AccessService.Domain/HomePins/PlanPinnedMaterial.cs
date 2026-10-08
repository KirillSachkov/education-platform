using Ordering;

namespace AccessService.Domain.HomePins;

/// <summary>
///     Aggregate root: материал, закреплённый ("закреп") автором на home-дашборде плана.
///     Per-plan упорядоченный список — порядок через fractional <see cref="SortKey"/>.
///     Только материалы (не внешние ссылки / markdown-блоки). Epic #397.
/// </summary>
public sealed class PlanPinnedMaterial
{
    public const int NOTE_MAX_LENGTH = 500;

    private PlanPinnedMaterial() { } // EF

    private PlanPinnedMaterial(
        Guid id,
        Guid planId,
        Guid materialId,
        string? note,
        SortKey sortKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        PlanId = planId;
        MaterialId = materialId;
        Note = note;
        SortKey = sortKey;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid MaterialId { get; private set; }

    /// <summary>Опциональная заметка автора под закреплённым материалом (≤500 символов).</summary>
    public string? Note { get; private set; }

    public SortKey SortKey { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    ///     Создаёт новый закреп. PK генерируется в factory (<see cref="Guid.CreateVersion7"/>) —
    ///     aggregate root сохраняется через <c>DbSet.AddAsync</c>, не через nav-collection,
    ///     поэтому заранее заполненный PK безопасен (см. backend-transactions.md правило 4).
    /// </summary>
    public static Result<PlanPinnedMaterial, Error> Create(
        Guid planId,
        Guid materialId,
        string? note,
        SortKey sortKey,
        DateTimeOffset now)
    {
        Result<string?, Error> noteResult = ValidateNote(note);
        if (noteResult.IsFailure) return noteResult.Error;

        return new PlanPinnedMaterial(
            Guid.CreateVersion7(),
            planId,
            materialId,
            noteResult.Value,
            sortKey,
            now);
    }

    /// <summary>Обновляет заметку. <c>null</c> очищает.</summary>
    public UnitResult<Error> UpdateNote(string? note, DateTimeOffset now)
    {
        Result<string?, Error> noteResult = ValidateNote(note);
        if (noteResult.IsFailure) return noteResult.Error;

        Note = noteResult.Value;
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    /// <summary>Меняет позицию закрепа (drag-n-drop reorder).</summary>
    public void Reorder(SortKey newSortKey, DateTimeOffset now)
    {
        SortKey = newSortKey;
        UpdatedAt = now;
    }

    private static Result<string?, Error> ValidateNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Result.Success<string?, Error>(null);
        }

        string trimmed = note.Trim();
        if (trimmed.Length > NOTE_MAX_LENGTH)
        {
            return HomePinErrors.NoteTooLong();
        }

        return Result.Success<string?, Error>(trimmed);
    }
}
