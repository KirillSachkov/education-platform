using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Materials;

/// <summary>
///     Факт просмотра материала анонимным посетителем. Ключ — пара (AnonymousId, MaterialId),
///     уникальна. AnonymousId — стабильный UUID, выставленный фронтом в cookie <c>plu_anon_id</c>.
///     Используется только для агрегата counter'а «общее число просмотров материала» (issue #234).
///     На user-scoped прогресс и каскады в <c>module_item_progress</c> не влияет.
/// </summary>
public sealed class AnonymousMaterialView : AggregateRoot
{
    public const int ANONYMOUS_ID_MAX_LENGTH = 64;

    private AnonymousMaterialView(string anonymousId, Guid materialId)
    {
        Id = Guid.CreateVersion7();
        AnonymousId = anonymousId;
        MaterialId = materialId;
        ViewedAt = DateTime.UtcNow;
        CreatedAt = ViewedAt;
    }

    // EF Core ctor
    private AnonymousMaterialView()
    {
        AnonymousId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string AnonymousId { get; private set; }

    public Guid MaterialId { get; private set; }

    public DateTime ViewedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Result<AnonymousMaterialView, Error> Create(string anonymousId, Guid materialId)
    {
        if (string.IsNullOrWhiteSpace(anonymousId))
        {
            return GeneralErrors.ValueIsRequired(nameof(anonymousId));
        }

        if (anonymousId.Length > ANONYMOUS_ID_MAX_LENGTH)
        {
            return GeneralErrors.ValueIsInvalid(nameof(anonymousId));
        }

        if (materialId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(materialId));
        }

        return new AnonymousMaterialView(anonymousId, materialId);
    }
}
