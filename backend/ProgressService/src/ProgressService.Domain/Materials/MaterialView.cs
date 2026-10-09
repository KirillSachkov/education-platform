using ProgressService.Domain.Materials.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Materials;

/// <summary>
/// Факт «контакта» пользователя с материалом. User-scoped: одна строка на пару
/// (UserId, MaterialId). Имеет два состояния (issue #285):
/// <list type="bullet">
///     <item>
///         <c>IsCompleted=false</c> — silent view: пользователь зашёл на страницу
///         материала. Питает публичный счётчик «N просмотров» (#234), но НЕ означает
///         «изучено», не каскадит <c>module_item_progress</c>,
///         не публикует <see cref="MaterialViewedEvent"/>.
///     </item>
///     <item>
///         <c>IsCompleted=true</c> — пользователь явно нажал «Отметить изученным».
///         Публикуется <see cref="MaterialViewedEvent"/>, который каскадит
///         <c>module_item_progress</c> по активным enrollment'ам.
///     </item>
/// </list>
/// Переход <c>false → true</c> идемпотентен: повторное явное «изучено» не дублирует
/// каскад. Обратный переход <c>true → false</c> — через <c>UnmarkMaterialViewedHandler</c>,
/// строка сохраняется (счётчик «N просмотров» учитывает оба состояния).
/// </summary>
public sealed class MaterialView : AggregateRoot
{
    private MaterialView(Guid userId, Guid materialId, bool isCompleted)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        MaterialId = materialId;
        ViewedAt = DateTime.UtcNow;
        CreatedAt = ViewedAt;
        IsCompleted = isCompleted;
        CompletedAt = isCompleted ? ViewedAt : null;
    }

    private MaterialView()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MaterialId { get; private set; }

    /// <summary>Время первого «контакта» (silent view или mark — что произошло раньше).</summary>
    public DateTime ViewedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// True, если пользователь явно отметил материал изученным. Источник правды для
    /// «изучено» бейджа и счётчиков прогресса курса. False для silent track-view'ов
    /// (mount detail-страницы).
    /// </summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Момент явной отметки «изучено». Null пока <c>IsCompleted=false</c>.</summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Создаёт silent view track (изначально <c>IsCompleted=false</c>). Не публикует
    /// <see cref="MaterialViewedEvent"/> — каскад только на явное «Отметить изученным».
    /// </summary>
    public static Result<MaterialView, Error> CreateTrack(Guid userId, Guid materialId)
    {
        UnitResult<Error> validation = ValidateIds(userId, materialId);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        return new MaterialView(userId, materialId, isCompleted: false);
    }

    /// <summary>
    /// Создаёт запись сразу как «изученную» (явный mark без предшествующего track-view'а).
    /// Поднимает <see cref="MaterialViewedEvent"/> для cascade на module_item_progress.
    /// </summary>
    public static Result<MaterialView, Error> CreateCompleted(Guid userId, Guid materialId)
    {
        UnitResult<Error> validation = ValidateIds(userId, materialId);
        if (validation.IsFailure)
        {
            return validation.Error;
        }

        MaterialView view = new(userId, materialId, isCompleted: true);
        view.RaiseDomainEvent(new MaterialViewedEvent(view.UserId, view.MaterialId, view.ViewedAt));
        return view;
    }

    /// <summary>
    /// Помечает существующий silent view как «изученный». Идемпотентно: если уже completed,
    /// возвращает <c>false</c> и не поднимает событие (cascade выполнялся при первом mark'е).
    /// </summary>
    /// <returns><c>true</c> если состояние изменилось (false → true), иначе <c>false</c>.</returns>
    public bool MarkAsCompleted()
    {
        if (IsCompleted)
        {
            return false;
        }

        IsCompleted = true;
        CompletedAt = DateTime.UtcNow;
        RaiseDomainEvent(new MaterialViewedEvent(UserId, MaterialId, ViewedAt));
        return true;
    }

    /// <summary>
    /// Снимает отметку «изучено», оставляя запись как silent view track (для счётчика).
    /// Идемпотентно: если уже не completed — no-op.
    /// </summary>
    /// <returns><c>true</c> если состояние изменилось (true → false), иначе <c>false</c>.</returns>
    public bool UnmarkAsCompleted()
    {
        if (!IsCompleted)
        {
            return false;
        }

        IsCompleted = false;
        CompletedAt = null;
        return true;
    }

    /// <summary>
    /// Backward-compat для legacy call-sites/тестов. Эквивалентен <see cref="CreateCompleted"/>.
    /// </summary>
    public static Result<MaterialView, Error> Create(Guid userId, Guid materialId) =>
        CreateCompleted(userId, materialId);

    private static UnitResult<Error> ValidateIds(Guid userId, Guid materialId)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (materialId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(materialId));
        }

        return UnitResult.Success<Error>();
    }
}