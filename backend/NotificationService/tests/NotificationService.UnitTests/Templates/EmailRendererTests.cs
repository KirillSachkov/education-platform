using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Notifications;

namespace NotificationService.UnitTests.Templates;

public sealed class EmailRendererTests
{
    private readonly EmailRenderer _renderer = new();

    [Fact]
    public void Render_TemplateWithoutEmailPart_Throws()
    {
        NotificationTemplate template = new(
            id: "test",
            type: NotificationType.Welcome,
            defaultChannels: NotificationChannel.InApp,
            inApp: new("title", "body"),
            email: null);

        Assert.Throws<InvalidOperationException>(() =>
            _renderer.Render(template, TemplateArgs.Empty));
    }

    [Fact]
    public void Render_BodyArgKey_DoesNotConsumeLayoutSlot()
    {
        // Защита от случайного arg'а с ключом "body" — sentinel @@CONTENT_BODY@@ должен
        // защитить layout-плейсхолдер {body} от подстановки arg'ом.
        NotificationTemplate template = NotificationTemplates.Welcome;
        TemplateArgs args = TemplateArgs.Of(
            ("displayName", "John"),
            ("frontendUrl", "https://example.com"),
            ("body", "EVIL_INJECTION_THAT_SHOULD_NOT_REPLACE_LAYOUT_SLOT"));

        RenderedMessage rendered = _renderer.Render(template, args);

        // Layout оборачивает inner HTML — должен содержать тег <h2> из welcome.html.
        Assert.Contains("<h2", rendered.Body, StringComparison.Ordinal);
        // EVIL_INJECTION не должен заменить layout-slot. Sentinel защищает.
        Assert.DoesNotContain("EVIL_INJECTION_THAT_SHOULD_NOT_REPLACE_LAYOUT_SLOT", rendered.Body, StringComparison.Ordinal);
        // Inner HTML с {displayName} должен корректно подставиться.
        Assert.Contains("John", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_FrontendUrlWithQueryString_NotHtmlEscapedInLayout()
    {
        // Layout.html использует {frontendUrl} в logo-link (<a href="{frontendUrl}">) —
        // этот слот НЕ HTML-escape'ится (чтобы query-string `&` не превратился в `&amp;`
        // и URL не сломался). Inner-body шаблона welcome.html использует {openUrl}, а не
        // {frontendUrl}, поэтому сам query тут не появится — проверяем только host в layout.
        NotificationTemplate template = NotificationTemplates.Welcome;
        TemplateArgs args = TemplateArgs.Of(
            ("displayName", "John"),
            ("frontendUrl", "https://example.com/?utm=email&campaign=welcome"),
            ("openUrl", "https://example.com/n/abc123"));

        RenderedMessage rendered = _renderer.Render(template, args);

        // Layout logo-link должен содержать URL с pristine `&` (не `&amp;`) — layout-args
        // не HtmlEncode'ятся в EmailRenderer.
        Assert.Contains("?utm=email&campaign=welcome", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("utm=email&amp;campaign=welcome", rendered.Body, StringComparison.Ordinal);
        // Plain-text — тоже pristine.
        Assert.Contains("?utm=email&campaign=welcome", rendered.PlainTextBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_UserSuppliedValueInInnerBody_IsHtmlEscaped()
    {
        // Inner-body args должны escape'ться (XSS protection).
        NotificationTemplate template = NotificationTemplates.Welcome;
        TemplateArgs args = TemplateArgs.Of(
            ("displayName", "<script>alert(1)</script>"),
            ("frontendUrl", "https://example.com"));

        RenderedMessage rendered = _renderer.Render(template, args);

        Assert.DoesNotContain("<script>alert(1)</script>", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PlainTextBody_PopulatedFromEmailPlainTextOrInAppFallback()
    {
        NotificationTemplate template = NotificationTemplates.Welcome;
        TemplateArgs args = TemplateArgs.Of(
            ("displayName", "John"),
            ("frontendUrl", "https://example.com"));

        RenderedMessage rendered = _renderer.Render(template, args);

        Assert.NotNull(rendered.PlainTextBody);
        Assert.Contains("John", rendered.PlainTextBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_RawArg_HtmlNotEscaped_NormalArgStillEscaped()
    {
        // #532: raw-аргумент дайджеста несёт готовый html-список ссылок — HtmlEncode
        // пропускается; обычные args по-прежнему экранируются.
        NotificationTemplate template = NotificationTemplates.WeeklyDigest;
        TemplateArgs args = TemplateArgs.Of(
                ("digestSummary", "2 <новых> материала"),
                ("digestItems", "• «Title»"),
                ("openUrl", "https://example.com/n/1"),
                ("frontendUrl", "https://example.com"))
            .WithRaw(
                "digestItemsHtml",
                "<ul><li><a href=\"https://example.com/learn/1\">«Title»</a></li></ul>");

        RenderedMessage rendered = _renderer.Render(template, args);

        Assert.Contains(
            "<a href=\"https://example.com/learn/1\">«Title»</a>",
            rendered.Body, StringComparison.Ordinal);
        Assert.Contains("2 &lt;новых&gt; материала", rendered.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression: buyer email (plan-grant-received.html) содержит сводку о доступе,
    /// CTA-кнопку с упоминанием онбординга и ссылку поддержки на t.me/sachkov_blog.
    ///
    /// OnboardingOverlay монтируется в <c>(app)/layout.tsx</c> и автоматически показывает
    /// onboarding-визард на ЛЮБОЙ аутентифицированной странице (включая /courses/* и /home).
    /// Поэтому {openUrl} (→ /courses/{slug} или /home) гарантированно запускает onboarding
    /// при наличии pending onboarding у пользователя. CTA явно упоминает онбординг.
    /// </summary>
    [Fact]
    public void Render_PlanGrantReceived_ContainsPlanSummaryAndOnboardingCtaAndSupportLink()
    {
        NotificationTemplate template = NotificationTemplates.PlanGrantReceived;
        // planSummary без спецсимволов HTML: EmailRenderer кодирует значения через
        // WebUtility.HtmlEncode, поэтому «» → &laquo;&raquo; меняли бы raw-assert.
        const string planSummary = "Открыт доступ к курсу ASP.NET Core";
        TemplateArgs args = TemplateArgs.Of(
            ("planSummary", planSummary),
            ("openUrl", "https://sachkov-learn.net/n/abc-123"),
            ("frontendUrl", "https://sachkov-learn.net"));

        RenderedMessage rendered = _renderer.Render(template, args);

        // Сводка о купленном доступе присутствует в теле.
        Assert.Contains(planSummary, rendered.Body, StringComparison.Ordinal);
        // CTA-кнопка явно упоминает онбординг — пользователь понимает, что визард откроется.
        Assert.Contains("онбординг", rendered.Body, StringComparison.OrdinalIgnoreCase);
        // CTA ссылается на {openUrl} (redirect proxy → /courses/{slug} или /home,
        // оба страницы покрыты OnboardingOverlay в глобальном layout'е).
        Assert.Contains("href=\"https://sachkov-learn.net/n/abc-123\"", rendered.Body, StringComparison.Ordinal);
        // Блок поддержки содержит ссылку на Telegram-канал автора.
        Assert.Contains("t.me/sachkov_blog", rendered.Body, StringComparison.Ordinal);
        // Плейсхолдеры не утекли.
        Assert.DoesNotContain("{planSummary}", rendered.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("{openUrl}", rendered.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PlanGrantReceived_UserSuppliedPlanSummary_IsHtmlEscaped()
    {
        NotificationTemplate template = NotificationTemplates.PlanGrantReceived;
        TemplateArgs args = TemplateArgs.Of(
            ("planSummary", "Курс <XSS>"),
            ("openUrl", "https://sachkov-learn.net/n/1"),
            ("frontendUrl", "https://sachkov-learn.net"));

        RenderedMessage rendered = _renderer.Render(template, args);

        // planSummary — user-derived (из PlanGrantCreated.PlanName), должен HTML-escape'ться.
        Assert.DoesNotContain("<XSS>", rendered.Body, StringComparison.Ordinal);
        Assert.Contains("&lt;XSS&gt;", rendered.Body, StringComparison.Ordinal);
    }
}