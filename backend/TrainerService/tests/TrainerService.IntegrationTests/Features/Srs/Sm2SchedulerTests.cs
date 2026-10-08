using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.IntegrationTests.Features.Srs;

/// <summary>
/// Pure unit tests for the SM-2-lite scheduler and the <see cref="QuestionStudyState"/> aggregate that
/// drives it (#568 Ф2). No DbContext, no Testcontainers — deterministic given an injected <c>now</c>.
/// </summary>
public sealed class Sm2SchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    // ── Pure Sm2Scheduler.Schedule ──────────────────────────────────────────────────────────

    [Fact]
    public void FirstSuccess_SchedulesOneDayOut_KeepsDefaultEase()
    {
        Sm2Scheduler.Sm2State start = new(Sm2Scheduler.DefaultEaseFactor, 0, 0);

        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(start, success: true, Now, out DateTimeOffset due);

        Assert.Equal(1, next.Repetitions);
        Assert.Equal(1, next.IntervalDays);
        Assert.Equal(2.5, next.EaseFactor, precision: 6);
        Assert.Equal(Now.AddDays(1), due);
    }

    [Fact]
    public void SecondSuccess_SchedulesSixDaysOut()
    {
        Sm2Scheduler.Sm2State afterFirst = new(2.5, 1, 1);

        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(afterFirst, success: true, Now, out DateTimeOffset due);

        Assert.Equal(2, next.Repetitions);
        Assert.Equal(6, next.IntervalDays);
        Assert.Equal(Now.AddDays(6), due);
    }

    [Fact]
    public void ThirdSuccess_ScalesPreviousIntervalByEase()
    {
        // reps==3 → interval = round(prevInterval * EF) = round(6 * 2.5) = 15.
        Sm2Scheduler.Sm2State afterSecond = new(2.5, 6, 2);

        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(afterSecond, success: true, Now, out DateTimeOffset due);

        Assert.Equal(3, next.Repetitions);
        Assert.Equal(15, next.IntervalDays);
        Assert.Equal(Now.AddDays(15), due);
    }

    [Fact]
    public void Failure_ResetsRepetitionsAndIntervalToTomorrow()
    {
        Sm2Scheduler.Sm2State matured = new(2.5, 15, 3);

        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(matured, success: false, Now, out DateTimeOffset due);

        Assert.Equal(0, next.Repetitions);
        Assert.Equal(1, next.IntervalDays);
        Assert.Equal(Now.AddDays(1), due);
        // q=1 lowers EF by 0.54 (2.5 → 1.96), still above the 1.3 floor.
        Assert.Equal(1.96, next.EaseFactor, precision: 6);
    }

    [Fact]
    public void Ease_ClampsAtFloor_OnRepeatedFailure()
    {
        // Already at the floor → another failure must not drop below 1.3.
        Sm2Scheduler.Sm2State atFloor = new(Sm2Scheduler.MinEaseFactor, 1, 0);

        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(atFloor, success: false, Now, out _);

        Assert.Equal(Sm2Scheduler.MinEaseFactor, next.EaseFactor, precision: 6);
    }

    // ── QuestionStudyState aggregate ────────────────────────────────────────────────────────

    [Fact]
    public void Create_StartsAsSeen_WithDefaultsAndRealId()
    {
        QuestionStudyState state = QuestionStudyState.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.NotEqual(Guid.Empty, state.Id);
        Assert.Equal(StudyStatus.SEEN, state.Status);
        Assert.Null(state.NextDueAt);
        Assert.Equal(0, state.TimesSeen);
        Assert.Equal(Sm2Scheduler.DefaultEaseFactor, state.EaseFactor, precision: 6);
        Assert.Equal(0, state.IntervalDays);
        Assert.Equal(0, state.Repetitions);
    }

    [Fact]
    public void RecordTestResult_Correct_MarksKnownAndSchedulesNextDay()
    {
        QuestionStudyState state = QuestionStudyState.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        state.RecordTestResult(correct: true, Now);

        Assert.Equal(StudyStatus.KNOWN, state.Status);
        Assert.Equal(1, state.TimesSeen);
        Assert.Equal(1, state.TimesKnown);
        Assert.Equal(0, state.TimesWrong);
        Assert.Equal(1, state.Repetitions);
        Assert.Equal(1, state.IntervalDays);
        Assert.Equal(Now.AddDays(1), state.NextDueAt);
        Assert.Equal(Now, state.LastSeenAt);
    }

    [Fact]
    public void TwoTestSuccesses_ProgressOneDayThenSixDays()
    {
        QuestionStudyState state = QuestionStudyState.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        state.RecordTestResult(correct: true, Now);
        Assert.Equal(Now.AddDays(1), state.NextDueAt);

        DateTimeOffset later = Now.AddDays(1);
        state.RecordTestResult(correct: true, later);

        Assert.Equal(2, state.Repetitions);
        Assert.Equal(6, state.IntervalDays);
        Assert.Equal(later.AddDays(6), state.NextDueAt);
        Assert.Equal(2, state.TimesKnown);
    }

    [Fact]
    public void RecordTestResult_Wrong_MarksWrongAndResetsSrs()
    {
        QuestionStudyState state = QuestionStudyState.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // Build up a streak first.
        state.RecordTestResult(correct: true, Now);
        state.RecordTestResult(correct: true, Now.AddDays(1));
        Assert.Equal(2, state.Repetitions);

        DateTimeOffset failAt = Now.AddDays(2);
        state.RecordTestResult(correct: false, failAt);

        Assert.Equal(StudyStatus.WRONG, state.Status);
        Assert.Equal(0, state.Repetitions);
        Assert.Equal(1, state.IntervalDays);
        Assert.Equal(failAt.AddDays(1), state.NextDueAt);
        Assert.Equal(1, state.TimesWrong);
        Assert.Equal(3, state.TimesSeen);
    }
}
