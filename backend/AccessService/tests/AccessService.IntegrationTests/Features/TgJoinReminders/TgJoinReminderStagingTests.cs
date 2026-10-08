using AccessService.Web.Jobs;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.IntegrationTests.Features.TgJoinReminders;

/// <summary>
///     Pure unit tests for <see cref="TgJoinReminderStaging.DueStage"/> (#616, ST-3). No host /
///     DB — just the age → stage decision used by <see cref="TgJoinReminderSweeper"/>.
/// </summary>
public sealed class TgJoinReminderStagingTests
{
    private static readonly TimeSpan First = TimeSpan.FromDays(2);
    private static readonly TimeSpan Second = TimeSpan.FromDays(9);

    [Fact]
    public void Zero_sent_below_first_window_is_not_due()
    {
        string? stage = TgJoinReminderStaging.DueStage(0, TimeSpan.FromDays(1), First, Second);
        Assert.Null(stage);
    }

    [Fact]
    public void Zero_sent_at_or_past_first_window_is_reminder1()
    {
        Assert.Equal(TgJoinReminderStages.Reminder1,
            TgJoinReminderStaging.DueStage(0, First, First, Second));
        Assert.Equal(TgJoinReminderStages.Reminder1,
            TgJoinReminderStaging.DueStage(0, TimeSpan.FromDays(5), First, Second));
    }

    [Fact]
    public void One_sent_below_second_window_is_not_due()
    {
        string? stage = TgJoinReminderStaging.DueStage(1, TimeSpan.FromDays(5), First, Second);
        Assert.Null(stage);
    }

    [Fact]
    public void One_sent_at_or_past_second_window_is_reminder2()
    {
        Assert.Equal(TgJoinReminderStages.Reminder2,
            TgJoinReminderStaging.DueStage(1, Second, First, Second));
        Assert.Equal(TgJoinReminderStages.Reminder2,
            TgJoinReminderStaging.DueStage(1, TimeSpan.FromDays(30), First, Second));
    }

    [Fact]
    public void Two_sent_is_terminal_never_due()
    {
        string? stage = TgJoinReminderStaging.DueStage(2, TimeSpan.FromDays(60), First, Second);
        Assert.Null(stage);
    }
}
