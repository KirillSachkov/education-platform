using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;

namespace NotificationService.UnitTests.Templates;

public sealed class InAppRendererTests
{
    private readonly InAppRenderer _renderer = new();

    [Fact]
    public void Render_SubstitutesPlaceholders()
    {
        TemplateArgs args = TemplateArgs.Of(("displayName", "Алиса"));

        RenderedMessage rendered = _renderer.Render(NotificationTemplates.Welcome, args);

        Assert.Equal("Добро пожаловать!", rendered.Title);
        Assert.Contains("Алиса", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MissingPlaceholderLeftAsLiteral()
    {
        // Если arg не передан — placeholder остаётся в тексте, не "съедает" символы вокруг.
        // Это intentional поведение TemplateRenderer.Substitute.
        RenderedMessage rendered = _renderer.Render(NotificationTemplates.Welcome, TemplateArgs.Empty);

        Assert.Contains("{displayName}", rendered.Body, StringComparison.Ordinal);
    }
}
