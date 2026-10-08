using SharedKernel.DomainEvents;

namespace TrainerService.Domain.Questions;

/// <summary>
/// Aggregate root: вопрос СОБСТВЕННОГО банка тренажёра (#623). Тренажёр больше не тянет
/// контент из ECS-квизов — у него свои вопросы, отдельные и не пересекающиеся с тестами
/// платформы. Вопрос принадлежит банку (<see cref="BankId"/> → <c>TopicBank</c>).
/// <para><see cref="Id"/> — глобальная идентичность вопроса: её хранят снапшоты session-item'ов,
/// закладки, study-state и ссылки мок-собеса. Грейдинг идёт по снапшоту ключа, собираемому из
/// <see cref="CorrectOptionIds"/> (choice) и <see cref="ReferenceAnswer"/> (text).</para>
/// </summary>
public sealed class TrainerQuestion : AggregateRoot
{
    private readonly List<TrainerQuestionOption> _options = [];

    private TrainerQuestion() { } // EF

    private TrainerQuestion(
        Guid id,
        Guid bankId,
        string stem,
        TrainerQuestionType type,
        string? referenceAnswer,
        string? explanation,
        QuestionDifficulty? difficulty,
        string? section,
        string sortKey)
    {
        Id = id;
        BankId = bankId;
        Stem = stem;
        Type = type;
        ReferenceAnswer = referenceAnswer;
        Explanation = explanation;
        Difficulty = difficulty;
        Section = section;
        SortKey = sortKey;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Банк-владелец вопроса (<c>TopicBank.Id</c>). FK с каскадным удалением.</summary>
    public Guid BankId { get; private set; }

    public string Stem { get; private set; } = null!;

    public TrainerQuestionType Type { get; private set; }

    /// <summary>Эталон для EXACT_TEXT (точное сравнение) и OPEN_TEXT (ориентир AI-грейда). Иначе null.</summary>
    public string? ReferenceAnswer { get; private set; }

    public string? Explanation { get; private set; }

    public QuestionDifficulty? Difficulty { get; private set; }

    public string? Section { get; private set; }

    /// <summary>Fractional sort key для порядка вопросов внутри банка.</summary>
    public string SortKey { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    ///     Материализованный флаг «бесплатный сэмпл» (#674): вопрос доступен НЕ-PRO пользователю как
    ///     часть free-доли темы (≈10% автогрейдимых вопросов на bucket сложности). Считается
    ///     детерминированно доменным сервисом <c>TrainerFreeAllocationPolicy</c> и пересчитывается
    ///     после каждой мутации состава темы. Default <c>false</c> (заперт за PRO до первого пересчёта).
    ///     OPEN_TEXT (развёрнутый/AI-грейд) <b>никогда</b> не free.
    /// </summary>
    public bool IsFreeSample { get; private set; }

    public IReadOnlyList<TrainerQuestionOption> Options => _options;

    /// <summary>
    ///     Id правильных вариантов (choice-вопрос) — ключ грейдинга. Пусто для текстовых типов.
    ///     Лениво материализуется один раз и кэшируется; кэш сбрасывается при смене вариантов
    ///     (<see cref="ApplyOptions"/>). Метод (а не свойство), т.к. возвращает копию коллекции (S2365).
    /// </summary>
    private IReadOnlyList<Guid>? _correctOptionIds;

    public IReadOnlyList<Guid> CorrectOptionIds =>
        _correctOptionIds ??= _options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();

    public static Result<TrainerQuestion, Error> Create(
        Guid bankId,
        string? stem,
        TrainerQuestionType type,
        string? referenceAnswer,
        string? explanation,
        QuestionDifficulty? difficulty,
        string? section,
        string sortKey,
        IReadOnlyList<(string Text, bool IsCorrect)> options)
    {
        UnitResult<Error> validation = Validate(stem, type, referenceAnswer, options);
        if (validation.IsFailure)
            return validation.Error;

        TrainerQuestion question = new(
            Guid.CreateVersion7(),
            bankId,
            stem!.Trim(),
            type,
            Normalize(referenceAnswer),
            Normalize(explanation),
            difficulty,
            Normalize(section),
            sortKey);

        question.ApplyOptions(type, options);
        return question;
    }

    public UnitResult<Error> Update(
        string? stem,
        TrainerQuestionType type,
        string? referenceAnswer,
        string? explanation,
        QuestionDifficulty? difficulty,
        string? section,
        IReadOnlyList<(string Text, bool IsCorrect)> options)
    {
        UnitResult<Error> validation = Validate(stem, type, referenceAnswer, options);
        if (validation.IsFailure)
            return validation.Error;

        Stem = stem!.Trim();
        Type = type;
        ReferenceAnswer = Normalize(referenceAnswer);
        Explanation = Normalize(explanation);
        Difficulty = difficulty;
        Section = Normalize(section);
        ApplyOptions(type, options);
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void Reorder(string sortKey)
    {
        SortKey = sortKey;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Ставит/снимает флаг бесплатного сэмпла (#674). Вызывается ТОЛЬКО доменным сервисом
    ///     <c>TrainerFreeAllocationPolicy</c> при пересчёте free-доли темы — не из авторского ввода.
    /// </summary>
    public void SetFreeSample(bool isFreeSample) => IsFreeSample = isFreeSample;

    /// <summary>Варианты материализуются только для choice-типов; текстовые вопросы их не несут.</summary>
    private void ApplyOptions(TrainerQuestionType type, IReadOnlyList<(string Text, bool IsCorrect)> options)
    {
        _options.Clear();
        _correctOptionIds = null; // сброс кэша — варианты меняются.
        if (type is not (TrainerQuestionType.SINGLE_CHOICE or TrainerQuestionType.MULTI_CHOICE))
            return;

        int sortIndex = 0;
        foreach ((string Text, bool IsCorrect) option in options.Where(o => !string.IsNullOrWhiteSpace(o.Text)))
            _options.Add(TrainerQuestionOption.Create(option.Text, option.IsCorrect, sortIndex++));
    }

    private static UnitResult<Error> Validate(
        string? stem,
        TrainerQuestionType type,
        string? referenceAnswer,
        IReadOnlyList<(string Text, bool IsCorrect)> options)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return TrainerServiceErrors.Question.StemRequired();

        bool isChoice = type is TrainerQuestionType.SINGLE_CHOICE or TrainerQuestionType.MULTI_CHOICE;
        if (isChoice)
        {
            List<(string Text, bool IsCorrect)> nonEmpty =
                options.Where(o => !string.IsNullOrWhiteSpace(o.Text)).ToList();
            if (nonEmpty.Count < 2)
                return TrainerServiceErrors.Question.OptionsRequired();

            int correct = nonEmpty.Count(o => o.IsCorrect);
            if (type == TrainerQuestionType.SINGLE_CHOICE && correct != 1)
                return TrainerServiceErrors.Question.SingleChoiceOneCorrect();
            if (type == TrainerQuestionType.MULTI_CHOICE && correct < 1)
                return TrainerServiceErrors.Question.MultiChoiceNeedsCorrect();

            return UnitResult.Success<Error>();
        }

        // Текстовые типы (EXACT_TEXT / OPEN_TEXT): вариантов быть не должно.
        if (options.Any(o => !string.IsNullOrWhiteSpace(o.Text)))
            return TrainerServiceErrors.Question.OptionsNotAllowed();

        if (type == TrainerQuestionType.EXACT_TEXT && string.IsNullOrWhiteSpace(referenceAnswer))
            return TrainerServiceErrors.Question.ReferenceRequired();

        return UnitResult.Success<Error>();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
