using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Notifications;

namespace NotificationService.UnitTests.Templates;

public sealed class TelegramRendererTests
{
    private readonly TelegramRenderer _renderer = new();

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("with_underscore", "with\\_underscore")]
    [InlineData("with*asterisk", "with\\*asterisk")]
    [InlineData("with`backtick", "with\\`backtick")]
    [InlineData("with[bracket", "with\\[bracket")]
    [InlineData("with]closing", "with\\]closing")]
    [InlineData("with(paren", "with\\(paren")]
    [InlineData("with)closing", "with\\)closing")]
    [InlineData("[click](evil)", "\\[click\\]\\(evil\\)")]  // защита от inline-link injection
    [InlineData("_test_", "\\_test\\_")]
    public void EscapeMarkdownV1_EscapesAllControlChars(string input, string expected)
    {
        string actual = TelegramRenderer.EscapeMarkdownV1(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Render_TemplateWithoutTelegramPart_Throws()
    {
        NotificationTemplate template = new(
            id: "test",
            type: NotificationType.Welcome,
            defaultChannels: NotificationChannel.InApp,
            inApp: new("title", "body"),
            telegram: null);

        Assert.Throws<InvalidOperationException>(() =>
            _renderer.Render(template, TemplateArgs.Empty));
    }

    [Fact]
    public void Render_EscapesUserValuesNotTemplate()
    {
        NotificationTemplate template = NotificationTemplates.MaterialPublished;
        TemplateArgs args = TemplateArgs.Of(
            ("courseTitle", "Course_Name"),       // должен escape'нуться
            ("materialTitle", "Material *Bold*"), // должен escape'нуться
            ("materialUrl", "https://example.com"));

        RenderedMessage rendered = _renderer.Render(template, args);

        // Шаблон содержит свои *bold* — не должны экранироваться.
        Assert.Contains("*Course\\_Name*", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("Material \\*Bold\\*", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RawArg_NotEscaped_NormalArgStillEscaped()
    {
        // #532: raw-аргумент дайджеста несёт готовые MarkdownV1-ссылки — escape пропускается.
        NotificationTemplate template = NotificationTemplates.WeeklyDigest;
        TemplateArgs args = TemplateArgs.Of(
                ("digestSummary", "2 новых_материала"), // обычный arg — escape'ится
                ("openUrl", "https://example.com/n/1"))
            .WithRaw("digestItemsTg", "• [«Title»](https://example.com/learn/1)");

        RenderedMessage rendered = _renderer.Render(template, args);

        Assert.Contains("• [«Title»](https://example.com/learn/1)", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("2 новых\\_материала", rendered.Body, StringComparison.Ordinal);
    }
}
