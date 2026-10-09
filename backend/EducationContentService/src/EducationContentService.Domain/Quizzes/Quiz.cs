using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Aggregate Root — квиз (тест) из упорядоченного набора вопросов.
///     Самостоятельная сущность уровня материала (#489): материал ссылается на квиз
///     через <c>materials.quiz_id</c> (блок «Проверь себя»), один квиз может
///     переиспользоваться несколькими материалами.
///     <see cref="AccessType"/> — собственный уровень доступа квиза (зеркало Material).
///     Вопросы хранятся JSONB-массивом (<see cref="QuizQuestion"/>), порядок массива — порядок показа.
/// </summary>
/// <remarks>
///     Инвариант публикации: квиз нельзя опубликовать без вопросов.
///     Инвариант набора: не больше <see cref="MAX_QUESTIONS"/> вопросов, id вопросов уникальны.
///     <see cref="UpdateQuestions"/> заменяет весь набор целиком (простейший MVP-контракт).
/// </remarks>
public sealed class Quiz
{
    public const int MAX_QUESTIONS = 50;
    public const int DEFAULT_PASSING_SCORE_PERCENT = 70;

    private Quiz(
        Guid authorId,
        Title title,
        int passingScorePercent,
        QuizPurpose purpose,
        AccessType accessType,
        Guid? id)
    {
        Id = id ?? Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        PassingScorePercent = passingScorePercent;
        Purpose = purpose;
        AccessType = accessType;
        Status = PublicationStatus.DRAFT;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Quiz()
    {
    }

    public Guid Id { get; }

    public Guid AuthorId { get; }

    public Title Title { get; private set; } = null!;

    public PublicationStatus Status { get; private set; }

    /// <summary>Проходной балл в процентах (0..100).</summary>
    public int PassingScorePercent { get; private set; }

    /// <summary>
    ///     Назначение квиза. Immutable после создания (как <c>Course.Kind</c>).
    /// </summary>
    public QuizPurpose Purpose { get; }

    /// <summary>
    ///     Уровень доступа квиза — зеркало <c>Material.AccessType</c>
    ///     (PUBLIC | REGISTERED | ENROLLED). Redis-теги (resource type <c>quiz</c>)
    ///     синкаются событиями quiz.published / quiz.access_changed / quiz.hard_deleted (#490);
    ///     теги существуют только у PUBLISHED-квизов.
    /// </summary>
    public AccessType AccessType { get; private set; }

    /// <summary>Упорядоченный набор вопросов (JSONB, см. QuizQuestionsJsonConverter).</summary>
    public IReadOnlyList<QuizQuestion> Questions { get; private set; } = [];

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     Создаёт standalone-квиз в статусе <see cref="PublicationStatus.DRAFT"/>.
    ///     Привязка к материалу — на стороне материала (<c>materials.quiz_id</c>, UpdateMaterial).
    ///     Пустой набор вопросов допустим — гейт на публикации.
    ///     <paramref name="id"/> позволяет задать well-known идентификатор
    ///     <c>null</c> — обычная генерация <see cref="Guid.CreateVersion7()"/>.
    /// </summary>
    public static Result<Quiz, Error> Create(
        Guid authorId,
        Title title,
        IReadOnlyList<QuizQuestion> questions,
        int passingScorePercent = DEFAULT_PASSING_SCORE_PERCENT,
        QuizPurpose purpose = QuizPurpose.MATERIAL_CHECK,
        AccessType accessType = AccessType.PUBLIC,
        Guid? id = null)
    {
        if (passingScorePercent is < 0 or > 100)
            return EducationErrors.InvalidQuizPassingScore();

        var quiz = new Quiz(authorId, title, passingScorePercent, purpose, accessType, id);

        UnitResult<Error> questionsResult = quiz.UpdateQuestions(questions);
        if (questionsResult.IsFailure)
            return questionsResult.Error;

        return quiz;
    }

    /// <summary>
    ///     Обновляет заголовок, проходной балл и уровень доступа.
    /// </summary>
    public UnitResult<Error> Update(
        Title title,
        int passingScorePercent,
        AccessType accessType)
    {
        if (passingScorePercent is < 0 or > 100)
            return EducationErrors.InvalidQuizPassingScore();

        Title = title;
        PassingScorePercent = passingScorePercent;
        AccessType = accessType;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Заменяет весь упорядоченный набор вопросов целиком (MVP-контракт:
    ///     никаких частичных patch'ей отдельных вопросов).
    /// </summary>
    public UnitResult<Error> UpdateQuestions(IReadOnlyList<QuizQuestion> questions)
    {
        if (Status == PublicationStatus.PUBLISHED && questions.Count == 0)
            return EducationErrors.CannotPublishEmptyQuiz();

        if (questions.Count > MAX_QUESTIONS)
            return EducationErrors.QuizQuestionsLimitExceeded(MAX_QUESTIONS);

        if (questions.Select(q => q.Id).Distinct().Count() != questions.Count)
            return EducationErrors.QuizQuestionInvalid("идентификаторы вопросов должны быть уникальными");

        Questions = [.. questions];
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Переводит квиз в <see cref="PublicationStatus.PUBLISHED"/>.
    ///     Разрешено из <see cref="PublicationStatus.DRAFT"/> или <see cref="PublicationStatus.ARCHIVED"/>.
    ///     Требует хотя бы один вопрос.
    /// </summary>
    public UnitResult<Error> Publish()
    {
        if (Status != PublicationStatus.DRAFT && Status != PublicationStatus.ARCHIVED)
            return EducationErrors.InvalidQuizStatusTransition(
                Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        if (Questions.Count == 0)
            return EducationErrors.CannotPublishEmptyQuiz();

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }
}