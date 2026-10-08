using AssignmentReviewService.Core.Features.Reviews.Services;

namespace AssignmentReviewService.UnitTests.Reviews;

public sealed class AiReviewTextEncodingRepairTests
{
    [Theory]
    [InlineData("ÐžÑˆÐ¸Ð±ÐºÐ° ÑÐ¾Ñ…Ñ€Ð°Ð½ÐµÐ½Ð¸Ñ Ð´Ð°Ð½Ð½Ñ‹Ñ…", "Ошибка сохранения данных")]
    [InlineData("Ð”Ð¾Ð±Ð°Ð²ÑŒÑ‚Ðµ Ð¿Ñ€Ð¾Ð²ÐµÑ€ÐºÑƒ UnitResultErrors", "Добавьте проверку UnitResultErrors")]
    public void RepairUtf8Mojibake_RestoresRussianText(string mojibake, string expected)
    {
        string repaired = AiReviewTextEncodingRepair.RepairUtf8Mojibake(mojibake);

        Assert.Equal(expected, repaired);
        Assert.DoesNotContain("Ð", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("Ñ", repaired, StringComparison.Ordinal);
        Assert.DoesNotContain("�", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void RepairUtf8Mojibake_KeepsReadableMixedMarkdownUnchanged()
    {
        const string text = "Проверь Result.Failure(Errors.Failure(\"save.failure\", \"Ошибка сохранения\")) и `UnitResultErrors`.";

        string repaired = AiReviewTextEncodingRepair.RepairUtf8Mojibake(text);

        Assert.Equal(text, repaired);
    }

    [Fact]
    public void RepairUtf8Mojibake_RepairsOnlyBrokenPartsInMixedText()
    {
        const string text = "Summary: ÐžÑˆÐ¸Ð±ÐºÐ° ÑÐ¾Ñ…Ñ€Ð°Ð½ÐµÐ½Ð¸Ñ Ð´Ð°Ð½Ð½Ñ‹Ñ… in `BindDepartmentLocationHandler`.";

        string repaired = AiReviewTextEncodingRepair.RepairUtf8Mojibake(text);

        Assert.Equal("Summary: Ошибка сохранения данных in `BindDepartmentLocationHandler`.", repaired);
    }

    // Прод-кейс #475 (iteration 019eb104-…9e73): провайдер съел байт 0x85 внутри
    // mojibake → «Ñ» остался без continuation-байта (d1 2e — невалидный UTF-8).
    // Раньше строгий декодер ронял ремонт ВСЕГО текста; теперь восстанавливаем
    // остальное, дыра становится U+FFFD.
    [Fact]
    public void RepairUtf8Mojibake_RepairsLossilyWhenProviderAteBytes()
    {
        const string text =
            "\u00D0\u0094\u00D0\u00BE\u00D0\u00B1\u00D0\u00B0\u00D0\u00B2\u00D0\u00BB\u00D0\u00B5\u00D0\u00BD"
            + " ExceptionMiddleware \u00E2\u0080\u0094 \u00D1\u008D\u00D1\u0082\u00D0\u00BE \u00D1. "
            + "\u00D0\u009D\u00D0\u00BE \u00D0\u00B2\u00D1\u0081\u00D1\u0091 \u00D0\u00B5\u00D1\u0089\u00D1\u0091"
            + " \u00D0\u00BF\u00D0\u00B0\u00D0\u00B4\u00D0\u00B0\u00D0\u00B5\u00D1\u0082";

        string repaired = AiReviewTextEncodingRepair.RepairUtf8Mojibake(text);

        Assert.Equal("Добавлен ExceptionMiddleware — это \uFFFD. Но всё ещё падает", repaired);
        Assert.DoesNotContain("\u00D0", repaired, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainsMojibakeArtifacts_TrueForLossyRepairAndRawMojibake()
    {
        Assert.True(AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(
            "это \uFFFD. Но всё ещё падает"));
        Assert.True(AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(
            "\u00D0\u009E\u00D1\u0088\u00D0\u00B8\u00D0\u00B1\u00D0\u00BA\u00D0\u00B0"));
    }

    [Fact]
    public void ContainsMojibakeArtifacts_FalseBelowMarkerThreshold()
    {
        // 2 маркера (один mojibake-слог "\u00D0\u00B8") — ниже порога ≥3, retry не дёргаем;
        // такой ошмёток чинит сама репарация без повторного LLM-вызова.
        Assert.False(AiReviewTextEncodingRepair.ContainsMojibakeArtifacts("\u00D0\u00B8 something plain"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Все замечания исправлены — отлично… Задание выполнено.")]
    [InlineData("Метод Create возвращает Guid вместо DepartmentResponse — поправьте контракт.")]
    [InlineData("Fix `launchSettings.json` — remove the trailing comma.")]
    public void ContainsMojibakeArtifacts_FalseForCleanText(string text)
    {
        Assert.False(AiReviewTextEncodingRepair.ContainsMojibakeArtifacts(text));
    }
}
