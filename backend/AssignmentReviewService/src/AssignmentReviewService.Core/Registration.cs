using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Features.Installations.UseCases;
using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Core.Features.Reviews.Services;
using AssignmentReviewService.Core.Features.Reviews.UseCases;
using AssignmentReviewService.Core.Features.Webhooks.UseCases;
using AssignmentReviewService.Core.Maintenance;
using AssignmentReviewService.Domain.AiSettings;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.GitHubApp;
using StackExchange.Redis;
using Core.Abstractions;

namespace AssignmentReviewService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);
        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);
        services.AddEndpoints(typeof(Registration).Assembly);

        // Install state store: Redis if IConnectionMultiplexer registered (Web layer
        // wires it conditionally в non-Testing env), else InMemory fallback.
        // Shared/GitHubApp (#296) обёртки — TData=InstallStateData (ARS-specific record).
        // Keyspace "ars" предотвращает collision с AccessService install-state'ом.
        services.AddSingleton<IInstallStateStore<InstallStateData>>(sp =>
        {
            IConnectionMultiplexer? redis = sp.GetService<IConnectionMultiplexer>();
            return redis is not null
                ? new RedisInstallStateStore<InstallStateData>(
                    redis,
                    sp.GetRequiredService<ILogger<RedisInstallStateStore<InstallStateData>>>(),
                    keyspace: "ars")
                : new InMemoryInstallStateStore<InstallStateData>(sp.GetRequiredService<TimeProvider>());
        });

        // Webhook + callback handler'ы регистрируются через AddHandlers только
        // если они реализуют ICommandHandler. CompleteInstallationHandler и
        // HandleGitHubWebhookHandler — простые scoped services без command-shape,
        // регистрируем явно.
        services.AddScoped<CompleteInstallationHandler>();
        services.AddScoped<HandleGitHubWebhookHandler>();

        // Phase 7 (#15) AI review pipeline.
        services.AddOptions<AssignmentReviewAiOptions>()
            .Bind(configuration.GetSection(AssignmentReviewAiOptions.SECTION_NAME))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Reviewer.Model)
                           && options.Reviewer.Model.Length <= AiModelSlot.MAX_MODEL_LENGTH
                           && options.Reviewer.Temperature is >= 0 and <= 2
                           && options.Reviewer.MaxOutputTokens is >= 1 and <= AiModelSlot.MAX_OUTPUT_TOKENS
                           && options.Reviewer.TimeoutSeconds is >= 1 and <= AiModelSlot.MAX_TIMEOUT_SECONDS,
                "AssignmentReviewAI:Reviewer values are outside safe bounds.")
            .Validate(
                options => options.ReviewerBasePrompt is not null
                           && options.ReviewerBasePrompt.Length <= 4000,
                "AssignmentReviewAI:ReviewerBasePrompt cannot exceed 4000 characters.")
            .Validate(
                options => ValidateLimits(options.Limits),
                "AssignmentReviewAI:Limits values are outside safe bounds.")
            .ValidateOnStart();
        services.AddScoped<RateLimitChecker>();
        services.AddScoped<AiReviewer>();
        services.AddScoped<RepoContextBuilder>();
        services.AddScoped<RunIterationHandler>();

        // Phase 7 snapshot consumers (Wolverine handler shape; не ICommandHandler).
        services.AddScoped<StoreIssueReviewSpecHandler>();
        services.AddScoped<StoreProjectGuidelinesHandler>();

        // Phase 8 (#15) ARS-side consumer of progress.events/issue_submission.awaiting_review.
        services.AddScoped<IssueSubmissionAwaitingReviewHandler>();

        // Phase 13+ (#15) cascade cleanup на issue.hard_deleted.
        services.AddScoped<IssueHardDeletedAssignmentReviewHandler>();

        // TimeProvider — singleton, нужен RateLimitChecker'у. В тестах
        // подменяется FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);

        // Phase 11 (#15) admin AI settings.
        services.AddMemoryCache();
        services.AddScoped<IAssignmentReviewAiModelSettingsResolver,
            AssignmentReviewAiModelSettingsResolver>();
        // Issue #328 — Redis pub/sub invalidation для cross-replica AI-settings cache.
        // Если Redis не зарегистрирован (тесты), no-op стартует.
        services.AddHostedService<AiSettingsCacheInvalidationSubscriber>();

        // Issue #474 — startup recovery: сбрасывает RUNNING → QUEUED для review'ов,
        // застрявших из-за рестарта контейнера mid-iteration (lock взят, SaveChanges нет).
        services.AddHostedService<StaleRunningReviewRecoveryService>();

        // Phase 12 (#15) observability — singleton metrics aggregator.
        services.AddSingleton<Diagnostics.AssignmentReviewMetrics>();

        // Issue #328 — Wolverine dead-letter cron cleanup.
        services.AddOptions<DeadLetterCleanupOptions>()
            .Bind(configuration.GetSection(DeadLetterCleanupOptions.SECTION_NAME))
            .Validate(
                options => !options.Enabled
                           || (options.MaxAge >= TimeSpan.FromHours(1)
                               && options.MaxAge <= TimeSpan.FromDays(365)
                               && options.Interval >= TimeSpan.FromMinutes(1)
                               && options.Interval <= TimeSpan.FromDays(30)),
                "AssignmentReviewMaintenance:DeadLetterCleanup values are outside safe bounds.")
            .ValidateOnStart();
        services.AddHostedService<DeadLetterCleanupBackgroundService>();

        return services;
    }

    private static bool ValidateLimits(AssignmentReviewLimits limits) =>
        limits.MaxDiffAdditions is >= 1 and <= 100_000
        && limits.HardMaxDiffAdditions >= limits.MaxDiffAdditions
        && limits.HardMaxDiffAdditions <= 1_000_000
        && limits.HardMaxFiles is >= 1 and <= 10_000
        && limits.ManualMaxDiffAdditions >= limits.HardMaxDiffAdditions
        && limits.ManualMaxDiffAdditions <= 1_000_000
        && limits.ManualMaxFiles >= limits.HardMaxFiles
        && limits.ManualMaxFiles <= 10_000
        && limits.MaxIterationsPerSubmission is >= 1 and <= 10_000
        && limits.MaxIterationsPerUserPerDay is >= 1 and <= 100_000
        && limits.MaxIterationsPerAuthorPerDay is >= -1 and <= 1_000_000
        && limits.MaxInlineComments is >= 0 and <= 100
        && limits.LlmMaxAttempts is >= 1 and <= 10
        && limits.LlmRetryDelaySeconds is >= 0 and <= 300
        && limits.StaleReviewMaxAgeMinutes is >= 1 and <= 1440
        && limits.StaleRecoveryIntervalSeconds is >= 30 and <= 86_400;
}
