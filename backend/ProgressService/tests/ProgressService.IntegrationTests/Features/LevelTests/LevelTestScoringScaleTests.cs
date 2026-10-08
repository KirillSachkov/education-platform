using ProgressService.Domain.LevelTests;

namespace ProgressService.IntegrationTests.Features.LevelTests;

/// <summary>
///     Чистые unit-тесты 6-ступенчатой шкалы level-test'а (#528): границы
///     <see cref="LevelTestScoring.ResolveLevel"/> по порогам seed-файла,
///     нормализация EXACT_TEXT и «персоны» поверх банка, повторяющего
///     секционную геометрию <c>SeedData/level-test.json</c> (8 секций,
///     difficulty-спред, 5 EXACT_TEXT; OPEN_TEXT в банке пока нет — развёрнутые
///     ответы заменены сложным выбором, фидбек владельца). Пороги и геометрия —
///     копия seed-значений: менять синхронно с файлом.
/// </summary>
public class LevelTestScoringScaleTests
{
    private static readonly IReadOnlyList<LevelTestLevelThreshold> _thresholds =
    [
        new("PRE_JUNIOR", 0),
        new("JUNIOR", 25),
        new("JUNIOR_PLUS", 50),
        new("MIDDLE", 65),
        new("MIDDLE_PLUS", 78),
        new("SENIOR", 95),
    ];

    /// <summary>
    ///     Секционная геометрия seed-банка: (difficulty, kind) на вопрос.
    ///     Kind: c = choice (SINGLE/MULTI), e = EXACT_TEXT, o = OPEN_TEXT.
    /// </summary>
    private static readonly Dictionary<string, (string Difficulty, char Kind)[]> _bankShape = new()
    {
        ["csharp-runtime"] = [("JUNIOR", 'c'), ("JUNIOR", 'e'), ("MIDDLE", 'e'), ("MIDDLE", 'e')],
        ["oop-design"] = [("JUNIOR", 'e'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c')],
        ["memory-gc"] = [("JUNIOR", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c')],
        ["async-concurrency"] = [("JUNIOR", 'c'), ("MIDDLE", 'e'), ("MIDDLE", 'c'), ("SENIOR", 'c')],
        ["data-sql"] = [("JUNIOR", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c')],
        ["web-services"] = [("JUNIOR", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c'), ("SENIOR", 'c')],
        ["testing"] = [("JUNIOR", 'c'), ("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c')],
        ["distributed"] = [("MIDDLE", 'c'), ("MIDDLE", 'c'), ("SENIOR", 'c'), ("SENIOR", 'c'), ("SENIOR", 'c'), ("SENIOR", 'c')],
    };

    [Theory]
    [InlineData(0, "PRE_JUNIOR")]
    [InlineData(24, "PRE_JUNIOR")]
    [InlineData(25, "JUNIOR")]
    [InlineData(50, "JUNIOR_PLUS")]
    [InlineData(64, "JUNIOR_PLUS")]
    [InlineData(65, "MIDDLE")]
    [InlineData(77, "MIDDLE")]
    [InlineData(78, "MIDDLE_PLUS")]
    [InlineData(94, "MIDDLE_PLUS")]
    [InlineData(95, "SENIOR")]
    [InlineData(100, "SENIOR")]
    public void ResolveLevel_SixLevelScale_PicksGreatestReachedThreshold(int percent, string expected)
    {
        Assert.Equal(expected, LevelTestScoring.ResolveLevel(_thresholds, percent));
    }

    [Theory]
    [InlineData("1, 4, 9", "149")]
    [InlineData("True False", "truefalse")]
    [InlineData("  True,\nfalse ", "truefalse")]
    [InlineData("Гав!", "гав")]
    public void NormalizeExactAnswer_IgnoresCaseWhitespaceAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, LevelTestScoring.NormalizeExactAnswer(input));
    }

    [Fact]
    public void GradeQuestions_ExactText_MatchesNormalizedAndCountsAsDeterministic()
    {
        Guid questionId = Guid.NewGuid();
        var questions = new List<LevelTestQuestionKey>
        {
            new(questionId, "EXACT_TEXT", "csharp-runtime", "MIDDLE", [], "1, 4, 9"),
        };
        var answers = new List<LevelTestAnswer>
        {
            LevelTestAnswer.Create(questionId, null, " 1,4 , 9 ").Value,
        };

        IReadOnlyList<LevelTestQuestionResult> results =
            LevelTestScoring.GradeQuestions(questions, answers);

        LevelTestQuestionResult result = Assert.Single(results);
        Assert.True(result.IsCorrect);
        Assert.False(result.PendingAi);
        Assert.Equal(2m, result.EarnedPoints);
    }

    [Theory]
    // Скипнул всё → базовый уровень шкалы.
    [InlineData("none", false, "PRE_JUNIOR")]
    // Только JUNIOR-вопросы (choice + exact): по жёсткой шкале #528 это ещё «до джуна».
    [InlineData("juniors", false, "PRE_JUNIOR")]
    // Вся junior+middle-механика: рекалибровка v2.4 (#541) добавила мидл-вопросов,
    // но по senior'у в каждой секции держат планку — верхний JUNIOR_PLUS.
    [InlineData("juniors-and-middles", false, "JUNIOR_PLUS")]
    // Опенов в банке нет → includeOpenText не меняет знаменатели.
    [InlineData("juniors-and-middles", true, "JUNIOR_PLUS")]
    // Все детерминированные вопросы верно → SENIOR в тизере.
    [InlineData("everything", false, "SENIOR")]
    public void ComputeTotals_SeedShapedBank_PersonasLandOnExpectedLevels(
        string persona,
        bool includeOpenText,
        string expectedLevel)
    {
        (List<LevelTestQuestionKey> questions, List<LevelTestAnswer> answers) = BuildPersona(persona);

        IReadOnlyList<LevelTestQuestionResult> results =
            LevelTestScoring.GradeQuestions(questions, answers);

        LevelTestTotals totals = LevelTestScoring.ComputeTotals(
            results,
            sectionKey => new LevelTestSectionMeta(sectionKey, LevelTestScoring.DEFAULT_SECTION_WEIGHT, null),
            _thresholds,
            fallbackCourseId: null,
            includeOpenText);

        Assert.Equal(expectedLevel, totals.Level);
    }

    /// <summary>Банк по геометрии seed + ответы персоны: верно на всё, что не выше её планки.</summary>
    private static (List<LevelTestQuestionKey> Questions, List<LevelTestAnswer> Answers) BuildPersona(
        string persona)
    {
        var questions = new List<LevelTestQuestionKey>();
        var answers = new List<LevelTestAnswer>();

        foreach ((string section, (string Difficulty, char Kind)[] shape) in _bankShape)
        {
            foreach ((string difficulty, char kind) in shape)
            {
                Guid questionId = Guid.NewGuid();
                Guid correctOption = Guid.NewGuid();
                string reference = $"ref-{questionId:N}";

                questions.Add(kind switch
                {
                    'c' => new LevelTestQuestionKey(
                        questionId, "SINGLE_CHOICE", section, difficulty, [correctOption]),
                    'e' => new LevelTestQuestionKey(
                        questionId, "EXACT_TEXT", section, difficulty, [], reference),
                    _ => new LevelTestQuestionKey(
                        questionId, "OPEN_TEXT", section, difficulty, []),
                });

                bool shouldAnswer = persona switch
                {
                    "juniors" => string.Equals(difficulty, "JUNIOR", StringComparison.Ordinal),
                    "juniors-and-middles" => !string.Equals(difficulty, "SENIOR", StringComparison.Ordinal),
                    "everything" => true,
                    _ => false,
                };

                // Персоны отвечают только на детерминированные вопросы — opens скипают.
                if (!shouldAnswer || kind == 'o')
                {
                    continue;
                }

                answers.Add(kind == 'c'
                    ? LevelTestAnswer.Create(questionId, [correctOption], null).Value
                    : LevelTestAnswer.Create(questionId, null, reference.ToUpperInvariant()).Value);
            }
        }

        return (questions, answers);
    }
}
