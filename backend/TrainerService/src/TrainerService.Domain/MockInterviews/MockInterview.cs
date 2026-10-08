using System.Text.RegularExpressions;
using SharedKernel.DomainEvents;

namespace TrainerService.Domain.MockInterviews;

/// <summary>
/// Aggregate root: симуляция собеседования (mock-interview) — именованный набор тем тренажёра,
/// по которым набирается кросс-тематический пул вопросов для прогона «как на собесе». В отличие
/// от MOCK-сессии по треку (<see cref="TrainingSessions.TrainingSession"/>, mode=MOCK) симуляция
/// не привязана к одному треку: её темы (<see cref="TopicIds"/>) могут быть из разных треков.
/// Unique Slug. DRAFT → PUBLISHED (<see cref="Publish"/>). #568.
/// </summary>
public sealed class MockInterview : AggregateRoot
{
    public const int SLUG_MAX_LENGTH = 200;
    public const int TITLE_MAX_LENGTH = 200;
    public const int DESCRIPTION_MAX_LENGTH = 2000;

    public const int MAX_QUESTIONS_PER_SESSION = 200;

    private static readonly Regex SlugRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private readonly List<MockInterviewQuestion> _questions = [];

    private MockInterview() { } // EF

    private MockInterview(
        Guid id,
        string slug,
        string title,
        string? description,
        IReadOnlyList<Guid> topicIds,
        int sortIndex)
    {
        Id = id;
        Slug = slug;
        Title = title;
        Description = description;
        TopicIds = topicIds;
        SortIndex = sortIndex;
        IsPublished = false;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public string Slug { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>Темы тренажёра, из банков которых набирается пул симуляции. Хранится как Postgres <c>uuid[]</c>.</summary>
    public IReadOnlyList<Guid> TopicIds { get; private set; } = [];

    /// <summary>
    ///     Курированный автором набор вопросов мок-собеса (явные ссылки на вопросы ECS-квизов).
    ///     Студенческая сессия тянет из него случайную подвыборку размера <see cref="QuestionsPerSession"/>.
    ///     #585.
    /// </summary>
    public IReadOnlyList<MockInterviewQuestion> Questions => _questions;

    /// <summary>
    ///     Сколько вопросов выдаётся студенту за сессию (случайная подвыборка из <see cref="Questions"/>).
    ///     <c>null</c> = показывать весь курированный набор. Диапазон 1..<see cref="MAX_QUESTIONS_PER_SESSION"/>.
    ///     #585.
    /// </summary>
    public int? QuestionsPerSession { get; private set; }

    public int SortIndex { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Result<MockInterview, Error> Create(
        string? slug,
        string? title,
        string? description,
        IReadOnlyList<Guid> topicIds,
        int sortIndex)
    {
        Result<string, Error> slugResult = ValidateSlug(slug);
        if (slugResult.IsFailure)
            return slugResult.Error;

        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        return new MockInterview(
            Guid.CreateVersion7(),
            slugResult.Value,
            titleResult.Value,
            descriptionResult.Value,
            topicIds,
            sortIndex);
    }

    public UnitResult<Error> UpdateDetails(string? title, string? description)
    {
        Result<string, Error> titleResult = ValidateTitle(title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<string?, Error> descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
            return descriptionResult.Error;

        Title = titleResult.Value;
        Description = descriptionResult.Value;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void SetTopics(IReadOnlyList<Guid> topicIds)
    {
        TopicIds = topicIds;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Полностью заменяет курированный набор вопросов: очищает старый и пере-добавляет в том
    ///     порядке, в котором передан caller (SortIndex = индекс). Дубли НЕ дедупятся здесь —
    ///     это ответственность caller'а/UI; один и тот же вопрос встретится в пуле столько раз,
    ///     сколько передан. #585.
    /// </summary>
    public void SetQuestions(IReadOnlyList<Guid> questionIds)
    {
        _questions.Clear();
        for (int i = 0; i < questionIds.Count; i++)
            _questions.Add(MockInterviewQuestion.Create(questionIds[i], i));

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Задаёт размер случайной подвыборки на сессию. <c>null</c> = весь курированный набор;
    ///     иначе 1..<see cref="MAX_QUESTIONS_PER_SESSION"/>. #585.
    /// </summary>
    public UnitResult<Error> SetQuestionsPerSession(int? n)
    {
        if (n is not null && (n < 1 || n > MAX_QUESTIONS_PER_SESSION))
            return TrainerServiceErrors.MockInterview.InvalidPerSession(MAX_QUESTIONS_PER_SESSION);

        QuestionsPerSession = n;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Публикует симуляцию (виден в хабе участникам). Требует хотя бы один источник вопросов —
    ///     курированный набор (<see cref="Questions"/>) ИЛИ темы (<see cref="TopicIds"/>) непусты,
    ///     иначе сессия стартовала бы без вопросов. #585.
    /// </summary>
    public UnitResult<Error> Publish()
    {
        if (IsPublished)
            return UnitResult.Success<Error>();

        if (_questions.Count == 0 && TopicIds.Count == 0)
            return TrainerServiceErrors.MockInterview.NoQuestionSource();

        IsPublished = true;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void Unpublish()
    {
        if (!IsPublished)
            return;

        IsPublished = false;
        UpdatedAt = DateTime.UtcNow;
    }

    private static Result<string, Error> ValidateSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.MockInterview.SlugRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > SLUG_MAX_LENGTH)
            return TrainerServiceErrors.MockInterview.SlugTooLong(SLUG_MAX_LENGTH);

        if (!SlugRegex.IsMatch(trimmed))
            return TrainerServiceErrors.MockInterview.SlugInvalid();

        return trimmed;
    }

    private static Result<string, Error> ValidateTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TrainerServiceErrors.MockInterview.TitleRequired();

        string trimmed = raw.Trim();
        if (trimmed.Length > TITLE_MAX_LENGTH)
            return TrainerServiceErrors.MockInterview.TitleTooLong(TITLE_MAX_LENGTH);

        return trimmed;
    }

    private static Result<string?, Error> ValidateDescription(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string?)null;

        string trimmed = raw.Trim();
        if (trimmed.Length > DESCRIPTION_MAX_LENGTH)
            return TrainerServiceErrors.MockInterview.DescriptionTooLong(DESCRIPTION_MAX_LENGTH);

        return trimmed;
    }
}
