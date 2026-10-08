using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / plan_grant.created</c> → одно уведомление пользователю о
/// получении доступа по плану. Заменяет N per-course <c>CourseEnrolled</c>-уведомлений
/// когда юзеру открываются несколько курсов одним plan-grant'ом.
///
/// Текст (#485): называет конкретный продукт — «Открыт доступ к интенсиву «X»» — по
/// денормализованным <c>PlanName</c> (#445) + <c>OfferType</c> (курс / интенсив / марафон).
/// Длинный onboarding-чеклист из тела убран — по шагам ведёт onboarding-визард (#68)
/// на платформе, а уведомление deep-link'ает сразу в курс (single-course план) или на
/// <c>/home</c>: courseSlug резолвится через ECS и кладётся в payload, откуда
/// <c>PlatformLinkBuilder</c> печёт targetUrl. ECS down → slug null → fallback /home.
///
/// Tier-фоллбэки (старые envelopes без PlanName/OfferType):
/// <list type="bullet">
///   <item><c>FULL_ALL</c> → «Открыт полный доступ к программе .NET Fullstack»</item>
///   <item><c>LEARN_ALL</c> → «Открыт доступ ко всем материалам .NET Fullstack»</item>
///   <item><c>COURSE</c> → «Открыт доступ к курсу»</item>
/// </list>
/// </summary>
public sealed class PlanGrantReceivedHandler
{
    private const string FULL_ALL = "FULL_ALL";
    private const string LEARN_ALL = "LEARN_ALL";
    private const string COURSE_TIER = "COURSE";

    private const string OFFER_INTENSIVE = "INTENSIVE";
    private const string OFFER_MARATHON = "MARATHON";

    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<PlanGrantReceivedHandler> _logger;

    public PlanGrantReceivedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<PlanGrantReceivedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(PlanGrantCreated evt, CancellationToken ct)
    {
        string summary = BuildSummary(evt);

        // Deep-link: план с ровно одним курсом (обычный кейс для курса/интенсива/марафона)
        // ведёт прямо на страницу курса. Бандлы и full-access планы — на /home.
        string? courseSlug = null;
        string? authorSlug = null;
        if (evt.EffectiveCourseIds.Count == 1)
        {
            CourseRouteContext course = await NotificationRouteContextResolver.ResolveCourseAsync(
                _ecsClient,
                _authClient,
                evt.EffectiveCourseIds[0],
                _logger,
                ct);
            courseSlug = course.CourseSlug;
            authorSlug = course.AuthorSlug;
        }

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.PlanGrantReceived,
            recipientUserId: evt.UserId,
            correlationId: evt.GrantId,
            args: TemplateArgs.Of(("planSummary", summary)),
            payload: new
            {
                grantId = evt.GrantId,
                planId = evt.PlanId,
                planTier = evt.PlanTier,
                offerType = evt.OfferType,
                courseSlug,
                authorSlug,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private static string BuildSummary(PlanGrantCreated evt)
    {
        string? planName = string.IsNullOrWhiteSpace(evt.PlanName) ? null : evt.PlanName.Trim();

        return evt.PlanTier switch
        {
            FULL_ALL => "Открыт полный доступ к программе .NET Fullstack",
            LEARN_ALL => "Открыт доступ ко всем материалам .NET Fullstack",
            COURSE_TIER => BuildCourseSummary(evt.OfferType, planName),
            _ when planName is not null => $"Открыт доступ: «{planName}»",
            _ => "Открыт доступ к учебным материалам",
        };
    }

    private static string BuildCourseSummary(string? offerType, string? planName)
    {
        string noun = offerType switch
        {
            OFFER_INTENSIVE => "интенсиву",
            OFFER_MARATHON => "марафону",
            _ => "курсу",
        };

        return planName is null
            ? $"Открыт доступ к {noun}"
            : $"Открыт доступ к {noun} «{planName}»";
    }
}
