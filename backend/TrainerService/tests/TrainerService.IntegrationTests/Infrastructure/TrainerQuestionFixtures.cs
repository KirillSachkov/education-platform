using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrainerService.Core.Configuration;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Infrastructure.Postgres;
using Ordering;

namespace TrainerService.IntegrationTests.Infrastructure;

/// <summary>
///     Seeds local <see cref="TrainerQuestion"/> rows into a bank (the trainer's OWN question bank,
///     #623 — ECS is gone). Mirrors the old canned answer-key: one question per supported type
///     (SINGLE / MULTI / EXACT_TEXT / OPEN_TEXT) with known-correct answers, so grading stays
///     deterministic. Question/option ids are DB-generated, so callers read them back off the
///     returned <see cref="SeededQuestions"/> aggregates rather than from constants.
/// </summary>
public static class TrainerQuestionFixtures
{
    // Stable reference texts so tests can assert they DON'T leak into unanswered-session bodies.
    public const string SingleStem = "Что освобождает управляемую память в .NET?";
    public const string MultiStem = "Какие из перечисленного — value types?";
    public const string ExactStem = "Как называется механизм автоосвобождения памяти (англ.)?";
    public const string OpenStem = "Опишите, как работает поколенческий GC.";

    public const string ExactReference = "Garbage Collector";
    public const string OpenReference = "Эталонный развёрнутый ответ про GC.";
    public const string SingleExplanation = "GC освобождает память автоматически.";
    public const string MultiExplanation = "int и struct — value types; string — reference type.";

    /// <summary>The four seeded questions of a canonical bank, with real ids + options loaded.</summary>
    public sealed record SeededQuestions(
        TrainerQuestion SingleChoice,
        TrainerQuestion MultiChoice,
        TrainerQuestion ExactText,
        TrainerQuestion OpenText)
    {
        public Guid SingleQuestionId => SingleChoice.Id;
        public Guid SingleCorrectOption => SingleChoice.Options.First(o => o.IsCorrect).Id;
        public Guid SingleWrongOption => SingleChoice.Options.First(o => !o.IsCorrect).Id;

        public Guid MultiQuestionId => MultiChoice.Id;
        public IReadOnlyList<Guid> MultiCorrectOptions => MultiChoice.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();
        public Guid MultiWrongOption => MultiChoice.Options.First(o => !o.IsCorrect).Id;

        public Guid ExactQuestionId => ExactText.Id;
        public Guid OpenQuestionId => OpenText.Id;
    }

    /// <summary>
    ///     Inserts the canonical 4-question set into <paramref name="bankId"/> via a fresh DbContext,
    ///     then re-queries them (with Options) so the caller sees DB-generated ids. Idempotent only in
    ///     the sense that each call adds a fresh set — call once per bank.
    /// </summary>
    public static async Task<SeededQuestions> SeedFourQuestionsAsync(IntegrationTestsWebFactory factory, Guid bankId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();

        TrainerQuestion single = Create(
            bankId, SingleStem, TrainerQuestionType.SINGLE_CHOICE, null, SingleExplanation,
            QuestionDifficulty.JUNIOR, "memory", SortKey.Initial().Value,
            [("Сборщик мусора", true), ("Деструктор вручную", false)]);

        TrainerQuestion multi = Create(
            bankId, MultiStem, TrainerQuestionType.MULTI_CHOICE, null, MultiExplanation,
            QuestionDifficulty.MIDDLE, "types", KeyAt(1),
            [("int", true), ("struct", true), ("string", false)]);

        TrainerQuestion exact = Create(
            bankId, ExactStem, TrainerQuestionType.EXACT_TEXT, ExactReference, "Garbage Collector (GC).",
            QuestionDifficulty.JUNIOR, "memory", KeyAt(2), []);

        TrainerQuestion open = Create(
            bankId, OpenStem, TrainerQuestionType.OPEN_TEXT, OpenReference, "Поколения 0/1/2, продвижение выживших.",
            QuestionDifficulty.SENIOR, "memory", KeyAt(3), []);

        await db.TrainerQuestions.AddRangeAsync(single, multi, exact, open);
        await db.SaveChangesAsync();

        // Mirror the production mutation handlers (#674) which the direct-insert fixture bypasses:
        // derive IsFreeSample so non-PRO paths see a free pool (JUNIOR→single, MIDDLE→multi; OPEN never).
        await RecomputeFreeSamplesAsync(factory, bankId);

        return new SeededQuestions(
            await ReloadAsync(factory, single.Id),
            await ReloadAsync(factory, multi.Id),
            await ReloadAsync(factory, exact.Id),
            await ReloadAsync(factory, open.Id));
    }

    /// <summary>
    ///     Inserts a single SINGLE_CHOICE question into <paramref name="bankId"/> with one correct +
    ///     one wrong option, the given stem/difficulty. Returns the reloaded aggregate (real ids).
    /// </summary>
    public static async Task<TrainerQuestion> SeedSingleChoiceAsync(
        IntegrationTestsWebFactory factory,
        Guid bankId,
        string stem,
        QuestionDifficulty difficulty = QuestionDifficulty.MIDDLE,
        string? section = null)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();

        TrainerQuestion question = Create(
            bankId, stem, TrainerQuestionType.SINGLE_CHOICE, null, null, difficulty, section,
            SortKey.Initial().Value, [("Верно", true), ("Неверно", false)]);

        await db.TrainerQuestions.AddAsync(question);
        await db.SaveChangesAsync();

        await RecomputeFreeSamplesAsync(factory, bankId);

        return await ReloadAsync(factory, question.Id);
    }

    /// <summary>
    ///     Inserts two OPEN_TEXT questions into <paramref name="bankId"/> (no auto-gradable types),
    ///     then recomputes free samples — leaving the topic with ZERO free samples (#674): OPEN_TEXT is
    ///     never a free sample. Exercises the "topic fully locked for non-PRO" branch of the topic list.
    /// </summary>
    public static async Task SeedOpenTextOnlyAsync(IntegrationTestsWebFactory factory, Guid bankId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();

        TrainerQuestion open1 = Create(
            bankId, OpenStem, TrainerQuestionType.OPEN_TEXT, OpenReference, "Поколения 0/1/2.",
            QuestionDifficulty.SENIOR, "memory", SortKey.Initial().Value, []);
        TrainerQuestion open2 = Create(
            bankId, "Опишите работу финализаторов.", TrainerQuestionType.OPEN_TEXT, "Эталон про финализаторы.",
            null, QuestionDifficulty.MIDDLE, "memory", KeyAt(1), []);

        await db.TrainerQuestions.AddRangeAsync(open1, open2);
        await db.SaveChangesAsync();

        await RecomputeFreeSamplesAsync(factory, bankId);
    }

    /// <summary>
    ///     Recomputes <see cref="TrainerQuestion.IsFreeSample"/> over the whole topic owning
    ///     <paramref name="bankId"/> via the domain policy (#674) — mirrors the production handlers
    ///     that the direct-insert fixtures bypass. Without it, fixture questions ship locked.
    /// </summary>
    public static async Task RecomputeFreeSamplesAsync(IntegrationTestsWebFactory factory, Guid bankId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();

        TopicBank? bank = await db.TopicBanks.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankId);
        if (bank is null)
            return;

        List<Guid> bankIds = await db.TopicBanks
            .Where(b => b.TopicId == bank.TopicId && b.Purpose == BankPurpose.STUDY)
            .Select(b => b.Id)
            .ToListAsync();

        List<TrainerQuestion> questions = await db.TrainerQuestions
            .Include(q => q.Options)
            .Where(q => bankIds.Contains(q.BankId))
            .ToListAsync();

        TrainerFreeAllocationPolicy.Recompute(questions, TrainerOptions.DEFAULT_FREE_SAMPLE_PERCENT);
        await db.SaveChangesAsync();
    }

    /// <summary>Re-queries one question (with Options) through a fresh DbContext so its DB ids are loaded.</summary>
    public static async Task<TrainerQuestion> ReloadAsync(IntegrationTestsWebFactory factory, Guid questionId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrainerServiceDbContext>();
        return await db.TrainerQuestions
            .Include(q => q.Options)
            .AsNoTracking()
            .SingleAsync(q => q.Id == questionId);
    }

    private static TrainerQuestion Create(
        Guid bankId,
        string stem,
        TrainerQuestionType type,
        string? referenceAnswer,
        string? explanation,
        QuestionDifficulty? difficulty,
        string? section,
        string sortKey,
        IReadOnlyList<(string Text, bool IsCorrect)> options) =>
        TrainerQuestion.Create(
            bankId, stem, type, referenceAnswer, explanation, difficulty, section, sortKey, options).Value;

    private static string KeyAt(int index)
    {
        SortKey key = SortKey.Initial();
        for (int i = 0; i < index; i++)
            key = SortKey.After(key);

        return key.Value;
    }
}
