using System.Data.Common;
using AccessService.Core.Database;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.Admin;

/// <summary>
/// <c>GET /access/admin/users/{userId}/post-purchase-status</c> — единый снимок состояния
/// пользователя после покупки для support/admin: оплаченные заказы, активные grant'ы, статус
/// онбординга по каждому плану, Telegram-членство (через TelegramBotService) и производные
/// диагностики («оплатил, но нет grant», «Telegram-членство не подтверждено», «онбординг не
/// создан»). Заменяет ручную диагностику инцидента «купил, но не попал в Telegram» через прод-БД
/// + Bot API (#444). Read-only; никогда не 500-ит на недоступности TelegramBotService (soft-degrade
/// → status=unknown).
/// </summary>
public sealed record GetUserPostPurchaseStatusQuery(Guid UserId) : IQuery;

public sealed record PostPurchaseOrderDto(
    Guid Id,
    Guid PlanId,
    string? PlanDisplayName,
    long AmountCents,
    string Status,
    DateTime CreatedAt,
    DateTime? PaidAt);

/// <summary>Состояние онбординга пользователя по конкретному плану.</summary>
public sealed record PostPurchaseOnboardingDto(
    bool FlowEnabled,
    bool Started,
    bool Completed,
    int TotalSteps,
    int CompletedSteps,
    int SkippedSteps,
    string? CurrentStepType,
    bool TelegramStepPending,
    bool GithubStepPending);

/// <summary>Telegram-членство по плану. <c>Status</c>: member | not_member | unknown | n/a.</summary>
public sealed record PostPurchaseTelegramDto(
    bool ChatBound,
    bool IsMember,
    string Status);

public sealed record PostPurchaseGrantDto(
    Guid GrantId,
    Guid PlanId,
    string? PlanDisplayName,
    string? PlanTier,
    string Source,
    DateTime GrantedAt,
    PostPurchaseOnboardingDto? Onboarding,
    PostPurchaseTelegramDto Telegram);

public sealed record PostPurchaseStatusResponse(
    Guid UserId,
    bool HasPaidOrder,
    IReadOnlyList<PostPurchaseOrderDto> Orders,
    IReadOnlyList<PostPurchaseGrantDto> ActiveGrants,
    IReadOnlyList<string> Diagnostics);

/// <summary>Dapper row для активных grant'ов (плоский, до обогащения онбордингом/Telegram).</summary>
public sealed record PostPurchaseActiveGrantRow(
    Guid GrantId,
    Guid PlanId,
    string? PlanDisplayName,
    string? PlanTier,
    string Source,
    DateTime GrantedAt);

public sealed class GetUserPostPurchaseStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/users/{userId:guid}/post-purchase-status",
                async Task<EndpointResult<PostPurchaseStatusResponse>> (
                    Guid userId,
                    [FromServices] GetUserPostPurchaseStatusHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserPostPurchaseStatusQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed class GetUserPostPurchaseStatusHandler
    : IQueryHandlerWithResult<PostPurchaseStatusResponse, GetUserPostPurchaseStatusQuery>
{
    private const string STATUS_NA = "n/a";
    private const string STATUS_UNKNOWN = "unknown";
    private const string STATUS_MEMBER = "member";
    private const string ORDER_PAID = "PAID";

    private readonly ITransactionManager _transactions;
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly ITelegramBotServiceClient _telegram;
    private readonly ILogger<GetUserPostPurchaseStatusHandler> _logger;

    public GetUserPostPurchaseStatusHandler(
        ITransactionManager transactions,
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITelegramBotServiceClient telegram,
        ILogger<GetUserPostPurchaseStatusHandler> logger)
    {
        _transactions = transactions;
        _onboardings = onboardings;
        _flows = flows;
        _telegram = telegram;
        _logger = logger;
    }

    public async Task<Result<PostPurchaseStatusResponse, Error>> Handle(
        GetUserPostPurchaseStatusQuery query,
        CancellationToken cancellationToken)
    {
        Guid userId = query.UserId;
        DbConnection connection = _transactions.GetDbConnection();

        const string ordersSql = """
            SELECT
                o.id,
                o.plan_id,
                p.display_name AS plan_display_name,
                o.amount_cents,
                o.status,
                o.created_at,
                o.paid_at
            FROM orders o
            LEFT JOIN plans p ON p.id = o.plan_id
            WHERE o.user_id = @UserId
            ORDER BY o.created_at DESC
            LIMIT 100;
            """;
        List<PostPurchaseOrderDto> orders =
            (await connection.QueryAsync<PostPurchaseOrderDto>(ordersSql, new { UserId = userId })).ToList();

        const string grantsSql = """
            SELECT
                g.id AS grant_id,
                g.plan_id,
                p.display_name AS plan_display_name,
                p.tier AS plan_tier,
                g.source,
                g.granted_at
            FROM plan_grants g
            LEFT JOIN plans p ON p.id = g.plan_id
            WHERE g.user_id = @UserId AND g.status = 'ACTIVE'
            ORDER BY g.granted_at DESC
            LIMIT 100;
            """;
        List<PostPurchaseActiveGrantRow> grantRows =
            (await connection.QueryAsync<PostPurchaseActiveGrantRow>(grantsSql, new { UserId = userId })).ToList();

        IReadOnlyList<UserPlanOnboarding> userOnboardings =
            await _onboardings.GetManyByAsync(o => o.UserId == userId, cancellationToken);
        Dictionary<Guid, UserPlanOnboarding> onboardingByPlan =
            userOnboardings.GroupBy(o => o.PlanId).ToDictionary(g => g.Key, g => g.First());

        var grantDtos = new List<PostPurchaseGrantDto>(grantRows.Count);
        var diagnostics = new List<string>();

        foreach (PostPurchaseActiveGrantRow grant in grantRows)
        {
            Result<PlanOnboardingFlow, Error> flowResult =
                await _flows.GetByAsync(f => f.PlanId == grant.PlanId, cancellationToken);
            PlanOnboardingFlow? flow = flowResult.IsSuccess ? flowResult.Value : null;
            onboardingByPlan.TryGetValue(grant.PlanId, out UserPlanOnboarding? onboarding);

            PostPurchaseOnboardingDto? onboardingDto = BuildOnboarding(flow, onboarding);
            PostPurchaseTelegramDto telegramDto =
                await BuildTelegramAsync(userId, grant.PlanId, flow, onboarding, cancellationToken);

            grantDtos.Add(new PostPurchaseGrantDto(
                grant.GrantId, grant.PlanId, grant.PlanDisplayName, grant.PlanTier,
                grant.Source, grant.GrantedAt, onboardingDto, telegramDto));

            string planName = grant.PlanDisplayName ?? grant.PlanId.ToString();
            if (flow is { IsEnabled: true } && onboarding is null)
                diagnostics.Add($"План «{planName}»: онбординг включён, но не создан для пользователя.");
            else if (onboardingDto is { FlowEnabled: true, Started: true, Completed: false })
                diagnostics.Add(
                    $"План «{planName}»: онбординг не завершён (текущий шаг {onboardingDto.CurrentStepType ?? "—"}).");
            if (telegramDto.ChatBound && !telegramDto.IsMember)
                diagnostics.Add(
                    $"План «{planName}»: Telegram-членство не подтверждено (status={telegramDto.Status}).");
        }

        HashSet<Guid> activePlanIds = grantRows.Select(g => g.PlanId).ToHashSet();
        foreach (PostPurchaseOrderDto order in orders
                     .Where(o => string.Equals(o.Status, ORDER_PAID, StringComparison.Ordinal)))
        {
            if (!activePlanIds.Contains(order.PlanId))
                diagnostics.Add(
                    $"Оплачен заказ по плану «{order.PlanDisplayName ?? order.PlanId.ToString()}», "
                    + "но активного grant нет — проверить выдачу доступа.");
        }

        bool hasPaidOrder = orders.Exists(o => string.Equals(o.Status, ORDER_PAID, StringComparison.Ordinal));

        return new PostPurchaseStatusResponse(userId, hasPaidOrder, orders, grantDtos, diagnostics);
    }

    private static PostPurchaseOnboardingDto? BuildOnboarding(
        PlanOnboardingFlow? flow,
        UserPlanOnboarding? onboarding)
    {
        if (flow is null && onboarding is null)
            return null;

        bool enabled = flow?.IsEnabled ?? false;
        int totalSteps = flow?.Steps.Count ?? 0;
        int completedSteps = onboarding?.CompletedStepIds.Count ?? 0;
        int skippedSteps = onboarding?.SkippedStepIds.Count ?? 0;

        string? currentStepType = null;
        bool telegramPending = false;
        bool githubPending = false;

        if (flow is not null && onboarding is not null)
        {
            if (onboarding.CurrentStepId is Guid currentId)
                currentStepType = flow.Steps.FirstOrDefault(s => s.Id == currentId)?.Type.ToString();

            foreach (PlanOnboardingStep step in flow.Steps)
            {
                bool done = onboarding.CompletedStepIds.Contains(step.Id)
                    || onboarding.SkippedStepIds.Contains(step.Id);
                if (done)
                    continue;
                if (step.Type == PlanOnboardingStepType.TELEGRAM)
                    telegramPending = true;
                if (step.Type is PlanOnboardingStepType.GITHUB or PlanOnboardingStepType.GITHUB_REVIEW_APP)
                    githubPending = true;
            }
        }

        return new PostPurchaseOnboardingDto(
            enabled,
            Started: onboarding is not null,
            Completed: onboarding?.IsCompleted ?? false,
            totalSteps,
            completedSteps,
            skippedSteps,
            currentStepType,
            telegramPending,
            githubPending);
    }

    private async Task<PostPurchaseTelegramDto> BuildTelegramAsync(
        Guid userId,
        Guid planId,
        PlanOnboardingFlow? flow,
        UserPlanOnboarding? onboarding,
        CancellationToken ct)
    {
        // Авто-шаг TELEGRAM появляется в flow только когда у плана есть привязанный чат —
        // используем его наличие как признак «к плану привязана Telegram-группа».
        PlanOnboardingStep? telegramStep = flow?.Steps
            .FirstOrDefault(s => s.Type == PlanOnboardingStepType.TELEGRAM);
        if (telegramStep is null)
            return new PostPurchaseTelegramDto(ChatBound: false, IsMember: false, Status: STATUS_NA);

        if (onboarding is not null && onboarding.CompletedStepIds.Contains(telegramStep.Id))
            return new PostPurchaseTelegramDto(ChatBound: true, IsMember: true, Status: STATUS_MEMBER);

        try
        {
            Result<PlanMembershipDto, Error> membership =
                await _telegram.CheckPlanMembershipAsync(userId, planId, ct);
            if (membership.IsFailure)
                return new PostPurchaseTelegramDto(ChatBound: true, IsMember: false, Status: STATUS_UNKNOWN);

            return new PostPurchaseTelegramDto(
                ChatBound: true, membership.Value.IsMember, membership.Value.Status);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex,
                "post-purchase-status: Telegram membership check failed for user={UserId} plan={PlanId} — reporting unknown",
                userId, planId);
            return new PostPurchaseTelegramDto(ChatBound: true, IsMember: false, Status: STATUS_UNKNOWN);
        }
    }
}
