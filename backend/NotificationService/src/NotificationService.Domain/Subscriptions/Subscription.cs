using CSharpFunctionalExtensions;
using SharedKernel;

namespace NotificationService.Domain.Subscriptions;

/// <summary>
/// Подписка пользователя на сущность / User subscription to an entity.
/// </summary>
public sealed class Subscription
{
    private Subscription(SubscriptionId id, Guid userId, string entityType, Guid entityId)
    {
        Id = id;
        UserId = userId;
        EntityType = entityType;
        EntityId = entityId;
        CreatedAt = DateTime.UtcNow;
    }

    // EF Core
    private Subscription()
    {
    }

    /// <summary>
    /// Идентификатор подписки / Subscription identifier.
    /// </summary>
    public SubscriptionId Id { get; private set; } = null!;

    /// <summary>
    /// Идентификатор пользователя / User identifier.
    /// </summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// Тип сущности / Entity type.
    /// </summary>
    public string EntityType { get; private set; } = null!;

    /// <summary>
    /// Идентификатор сущности / Entity identifier.
    /// </summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// Дата и время создания / Creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Создаёт новую подписку / Creates a new subscription.
    /// </summary>
    public static Result<Subscription, Error> Create(Guid userId, string entityType, Guid entityId)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("subscription.user");

        if (string.IsNullOrWhiteSpace(entityType))
            return GeneralErrors.ValueIsRequired("subscription.entity.type");

        if (!SubscriptionEntityType.IsValid(entityType))
            return NotificationErrors.InvalidSubscriptionEntityType(entityType);

        if (entityId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("subscription.entity.id");

        return new Subscription(SubscriptionId.Create(), userId, entityType, entityId);
    }
}
