using TrainerService.Contracts.Limits;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Limits;

/// <summary>
///     GET /trainer/me/limits (#568) — остаток AI-лимитов + статус Trainer Pro для карточки в хабе.
///     Лимиты теста: Free grades=3/voice=0/mock=0; Pro voice=5, остальное unlimited (0 ⇒ null).
/// </summary>
public sealed class GetMyLimitsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Free_participant_sees_free_limits_and_is_not_pro()
    {
        EntitlementChecker.DenyAll(); // нет cap:TRAINER_PRO → free
        AuthenticateAs("platform-participant");

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));

        Assert.False(limits.IsPro);
        Assert.Equal(IntegrationTestsWebFactory.TestFreeOpenGradesPerDay, limits.OpenGrades.Limit);
        Assert.Equal(0, limits.OpenGrades.Used);
        // Голос/мок у free = 0 ⇒ по конвенции <=0 это Limit=null; карточка всё равно показывает
        // апселл для free (все AI-фичи за подпиской), числа неактуальны — их не проверяем.
    }

    [Fact]
    public async Task Pro_participant_sees_pro_limits()
    {
        // Дефолт фикстуры — GrantAll (есть cap:TRAINER_PRO).
        AuthenticateAs("platform-participant");

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));

        Assert.True(limits.IsPro);
        Assert.Equal(IntegrationTestsWebFactory.TestProVoiceMinutesPerMonth, limits.Voice.Limit);
        // Pro open-grades / mock безлимитны в тест-конфиге (0 ⇒ Limit=null).
        Assert.Null(limits.OpenGrades.Limit);
        Assert.Null(limits.Mock.Limit);
    }

    [Fact]
    public async Task Admin_is_subject_to_pro_limits_like_everyone()
    {
        AuthenticateAsAdmin();

        TrainerLimitsDto limits = await ReadResultAsync<TrainerLimitsDto>(
            await Client.GetAsync("/trainer/me/limits"));

        // Лимиты действуют для ВСЕХ, включая админа (#568, решение владельца): админ — Pro-тир,
        // а не безлимит. Голос в тест-конфиге Pro=5 → виден; open-grades/mock Pro=0 ⇒ безлимит.
        Assert.True(limits.IsPro);
        Assert.Equal(IntegrationTestsWebFactory.TestProVoiceMinutesPerMonth, limits.Voice.Limit);
        Assert.Null(limits.OpenGrades.Limit);
        Assert.Null(limits.Mock.Limit);
    }
}
