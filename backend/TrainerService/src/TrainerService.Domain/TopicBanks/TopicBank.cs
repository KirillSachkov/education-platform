using SharedKernel.DomainEvents;

namespace TrainerService.Domain.TopicBanks;

/// <summary>
/// Aggregate root: банк вопросов темы (#623). Несколько банков на тему — по сложности /
/// типу / уровню доступа. Вопросы (<c>TrainerQuestion.BankId</c>) принадлежат банку
/// локально — собственный контент тренажёра, не ECS-квизы; здесь связь + фримиум-tier.
/// </summary>
public sealed class TopicBank : AggregateRoot
{
    private TopicBank() { } // EF

    private TopicBank(
        Guid id,
        Guid topicId,
        BankTier tier,
        QuestionDifficulty? difficulty,
        string sortKey,
        BankPurpose purpose)
    {
        Id = id;
        TopicId = topicId;
        Tier = tier;
        Difficulty = difficulty;
        SortKey = sortKey;
        Purpose = purpose;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid TopicId { get; private set; }

    public BankTier Tier { get; private set; }

    public QuestionDifficulty? Difficulty { get; private set; }

    public string SortKey { get; private set; } = null!;

    /// <summary>
    ///     Назначение банка (STUDY — учебный список темы; MOCK — только в симуляции собеса).
    ///     По умолчанию STUDY. Переключается админом через <see cref="SetPurpose"/>.
    /// </summary>
    public BankPurpose Purpose { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static Result<TopicBank, Error> Create(
        Guid topicId,
        BankTier tier,
        QuestionDifficulty? difficulty,
        string sortKey,
        BankPurpose purpose = BankPurpose.STUDY) =>
        new TopicBank(
            Guid.CreateVersion7(),
            topicId,
            tier,
            difficulty,
            sortKey,
            purpose);

    public void SetPurpose(BankPurpose purpose) => Purpose = purpose;

    public void SetTier(BankTier tier) => Tier = tier;

    /// <summary>Обновляет атрибуты банка (admin). TopicId immutable.</summary>
    public void Update(BankTier tier, QuestionDifficulty? difficulty, BankPurpose purpose, string sortKey)
    {
        Tier = tier;
        Difficulty = difficulty;
        Purpose = purpose;
        SortKey = sortKey;
    }
}
