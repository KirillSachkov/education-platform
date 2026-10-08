using TrainerService.Contracts.Sessions;
using TrainerService.Core.Features.Sessions;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.IntegrationTests.Features.Questions;

/// <summary>
///     Pure unit tests (no Docker / DB) for the free-sample allocation policy (#674) and the
///     SessionMapper redaction of a locked item. Run fast outside Testcontainers.
/// </summary>
public sealed class TrainerFreeAllocationPolicyTests
{
    // --- TrainerFreeAllocationPolicy ---

    [Fact]
    public void Recompute_picks_first_by_sortkey_within_a_difficulty_bucket()
    {
        // 3 JUNIOR single-choice; 10% → ceil(0.3)=1 free → the lowest SortKey ("a").
        TrainerQuestion a = Single(QuestionDifficulty.JUNIOR, "a");
        TrainerQuestion b = Single(QuestionDifficulty.JUNIOR, "b");
        TrainerQuestion c = Single(QuestionDifficulty.JUNIOR, "c");

        TrainerFreeAllocationPolicy.Recompute([c, a, b], percent: 10); // unordered input

        Assert.True(a.IsFreeSample);
        Assert.False(b.IsFreeSample);
        Assert.False(c.IsFreeSample);
    }

    [Fact]
    public void Recompute_ceils_the_free_count()
    {
        // 11 eligible @ 10% → ceil(1.1) = 2 free.
        List<TrainerQuestion> bucket = Enumerable.Range(0, 11)
            .Select(i => Single(QuestionDifficulty.MIDDLE, $"k{i:D2}"))
            .ToList();

        TrainerFreeAllocationPolicy.Recompute(bucket, percent: 10);

        Assert.Equal(2, bucket.Count(q => q.IsFreeSample));
    }

    [Fact]
    public void Recompute_floors_free_count_at_one_per_bucket()
    {
        // 5 eligible @ 1% → ceil(0.05)=1, but min-1 guarantees a free taste anyway.
        List<TrainerQuestion> bucket = Enumerable.Range(0, 5)
            .Select(i => Single(QuestionDifficulty.SENIOR, $"k{i}"))
            .ToList();

        TrainerFreeAllocationPolicy.Recompute(bucket, percent: 1);

        Assert.Equal(1, bucket.Count(q => q.IsFreeSample));
    }

    [Fact]
    public void Recompute_honours_the_percent_knob()
    {
        List<TrainerQuestion> bucket = Enumerable.Range(0, 10)
            .Select(i => Single(QuestionDifficulty.JUNIOR, $"k{i}"))
            .ToList();

        TrainerFreeAllocationPolicy.Recompute(bucket, percent: 50);

        Assert.Equal(5, bucket.Count(q => q.IsFreeSample));
    }

    [Fact]
    public void Recompute_treats_each_difficulty_bucket_independently_including_null()
    {
        TrainerQuestion junior = Single(QuestionDifficulty.JUNIOR, "a");
        TrainerQuestion middle = Single(QuestionDifficulty.MIDDLE, "a");
        TrainerQuestion noDifficulty = Single(null, "a"); // null difficulty = its own bucket

        TrainerFreeAllocationPolicy.Recompute([junior, middle, noDifficulty], percent: 10);

        // Each single-question bucket yields exactly one free (min-1).
        Assert.True(junior.IsFreeSample);
        Assert.True(middle.IsFreeSample);
        Assert.True(noDifficulty.IsFreeSample);
    }

    [Fact]
    public void Recompute_never_frees_OPEN_TEXT_even_when_alone()
    {
        TrainerQuestion open = Make(TrainerQuestionType.OPEN_TEXT, QuestionDifficulty.JUNIOR, "a");

        TrainerFreeAllocationPolicy.Recompute([open], percent: 100);

        Assert.False(open.IsFreeSample);
    }

    [Fact]
    public void Recompute_excludes_OPEN_TEXT_from_buckets_so_closed_questions_still_get_a_free()
    {
        // A JUNIOR bucket of one closed + one open: only the closed one is eligible → it is free.
        TrainerQuestion closed = Single(QuestionDifficulty.JUNIOR, "a");
        TrainerQuestion open = Make(TrainerQuestionType.OPEN_TEXT, QuestionDifficulty.JUNIOR, "b");

        TrainerFreeAllocationPolicy.Recompute([closed, open], percent: 10);

        Assert.True(closed.IsFreeSample);
        Assert.False(open.IsFreeSample);
    }

    [Fact]
    public void Recompute_resets_stale_free_flags()
    {
        TrainerQuestion a = Single(QuestionDifficulty.JUNIOR, "a");
        TrainerQuestion b = Single(QuestionDifficulty.JUNIOR, "b");
        a.SetFreeSample(true);
        b.SetFreeSample(true); // both stale-true

        TrainerFreeAllocationPolicy.Recompute([a, b], percent: 10); // 2 @ 10% → 1 free

        Assert.True(a.IsFreeSample);
        Assert.False(b.IsFreeSample); // demoted on recompute
    }

    [Fact]
    public void Recompute_includes_EXACT_TEXT_as_eligible()
    {
        TrainerQuestion exact = Make(TrainerQuestionType.EXACT_TEXT, QuestionDifficulty.JUNIOR, "a");

        TrainerFreeAllocationPolicy.Recompute([exact], percent: 10);

        Assert.True(exact.IsFreeSample);
    }

    // --- SessionMapper redaction of a locked item (defense-in-depth: a stray OPEN_TEXT item) ---

    [Fact]
    public void SessionMapper_redacts_a_locked_open_item_for_a_non_pro_viewer()
    {
        TrainingSession session = BuildSessionWithOpenItem();

        SessionDto nonPro = SessionMapper.ToDto(session, hasPro: false);
        SessionItemDto item = Assert.Single(nonPro.Items);

        Assert.True(item.IsLocked);
        Assert.Equal("pro_required", item.LockReason);
        Assert.Null(item.QuestionText);   // redacted
        Assert.Empty(item.Options);       // redacted
        Assert.Null(item.ReferenceAnswer);
        Assert.Null(item.Explanation);
        Assert.Null(item.Feedback);
    }

    [Fact]
    public void SessionMapper_keeps_open_item_content_for_a_pro_viewer()
    {
        TrainingSession session = BuildSessionWithOpenItem();

        SessionDto pro = SessionMapper.ToDto(session, hasPro: true);
        SessionItemDto item = Assert.Single(pro.Items);

        Assert.False(item.IsLocked);
        Assert.Equal("Опишите GC", item.QuestionText);
    }

    private static TrainingSession BuildSessionWithOpenItem()
    {
        TrainingSession session = TrainingSession.Create(
            Guid.NewGuid(),
            TrainingMode.DRILL,
            trackId: Guid.NewGuid(),
            [Guid.NewGuid()],
            timeLimitSeconds: null,
            RevealPolicy.PER_QUESTION).Value;

        session.AddItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TrainerQuestionType.OPEN_TEXT.ToString(),
            "Опишите GC",
            optionsJson: "[]",
            section: null,
            difficulty: QuestionDifficulty.SENIOR.ToString(),
            sortIndex: 0,
            gradingKeyJson: null);

        return session;
    }

    private static TrainerQuestion Single(QuestionDifficulty? difficulty, string sortKey) =>
        Make(TrainerQuestionType.SINGLE_CHOICE, difficulty, sortKey);

    private static TrainerQuestion Make(TrainerQuestionType type, QuestionDifficulty? difficulty, string sortKey)
    {
        IReadOnlyList<(string, bool)> options = type is TrainerQuestionType.SINGLE_CHOICE
            ? [("a", true), ("b", false)]
            : [];
        string? reference = type == TrainerQuestionType.EXACT_TEXT ? "ref" : null;

        return TrainerQuestion.Create(
            Guid.NewGuid(), "stem", type, reference, explanation: null, difficulty, section: null, sortKey, options).Value;
    }
}
