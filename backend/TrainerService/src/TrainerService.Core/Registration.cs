using Core.Abstractions;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TrainerService.Core.Configuration;
using TrainerService.Core.Features.Questions;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Features.Stats;
using TrainerService.Core.Grading;

namespace TrainerService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireFiniteProductionLimits = false)
    {
        services.AddEndpoints(typeof(Registration).Assembly);
        services.AddHandlers(typeof(Registration).Assembly);
        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);

        // Service-specific AI settings (#585): transcription model + open-answer grading model.
        services.AddSingleton<IValidateOptions<TrainerAiOptions>>(
            new TrainerAiOptionsValidator(requireFiniteProductionLimits));
        services.AddOptions<TrainerAiOptions>()
            .Bind(configuration.GetSection(TrainerAiOptions.SECTION_NAME))
            .ValidateOnStart();

        // Monetization knobs (#674): free-sample percent. Recomputer re-derives per-question IsFreeSample
        // after every topic-composition mutation (question create/update/delete) + the backfill CLI.
        services.AddSingleton<IValidateOptions<TrainerOptions>, TrainerOptionsValidator>();
        services.AddOptions<TrainerOptions>()
            .Bind(configuration.GetSection(TrainerOptions.SECTION_NAME))
            .ValidateOnStart();
        services.AddScoped<TrainerFreeSampleRecomputer>();

        // Reusable AI grader of ONE open answer (#585/#568 W2) — shared by the mock background path
        // and the inline non-mock CheckAnswer path. Injectable so tests register a fake (no real LLM).
        services.AddScoped<IOpenAnswerGrader, OpenAnswerGrader>();

        // AI generator of a draft reference answer (эталон) for OPEN_TEXT questions (#691 t6) — used by
        // the admin backfill endpoint so the grader has something to compare and the UI shows «Эталонный
        // ответ». Mirrors OpenAnswerGrader: prompt + JSON schema live in the generator; tests stub IAiClient.
        services.AddScoped<IReferenceAnswerGenerator, ReferenceAnswerGenerator>();

        // Shared session-answer recording path (#585): grade (auto OR inline-AI) → record → mastery →
        // study-state → reveal-gated response. Used by CheckAnswer (typed) and SubmitVoiceAnswer (voice)
        // so a spoken open answer in LEARN/DRILL is graded identically to a typed one.
        services.AddScoped<SessionAnswerGrading>();

        // AI-usage ledger (#614 C1): per-user tokens + ₽ cost of each AI call. Best-effort recording at
        // the three call sites (open-answer grade, mock aggregate feedback, transcription). No quota
        // enforcement here — that is C2.
        services.AddSingleton<AiUsageCostCalculator>();
        services.AddScoped<AiUsageLedger>();

        // Per-user AI-usage quota enforcement (#614 C2): Redis INCR+EXPIRE counters gated before each
        // billable AI call (open grade / voice / mock). Fail-open on Redis error — a quota is a soft
        // cost-backstop, not a security gate. Reuses the IConnectionMultiplexer registered by B1.
        services.AddScoped<TrainerQuotaService>();
        services.AddSingleton<TrainerOpenGradeRateLimiter>();

        // AI grading of mock-interview open answers (#585). The queue is a singleton (channel shared
        // across scopes); the grading service is scoped (DbContext + transaction); a hosted service
        // drains the queue and runs the grader in its own scope, plus a startup-recovery sweep.
        services.AddSingleton<MockGradingQueue>();
        services.AddSingleton<IMockGradingQueue>(sp => sp.GetRequiredService<MockGradingQueue>());
        services.AddScoped<MockAnswerGradingService>();
        services.AddHostedService<MockGradingBackgroundService>();

        // Daily stat-snapshot job (#681 T1): one snapshot on startup, then once a day. Recomputes today's
        // platform-KPI / topic-mastery / question-accuracy rows idempotently via IStatSnapshotRepository.
        services.AddHostedService<StatsSnapshotBackgroundService>();

        // Cross-topic question-content lookup (stems from the trainer's own question bank) for SRS-due
        // / mistakes projections (#568 Ф2 / #623). Not auto-discovered — registered explicitly.
        services.AddScoped<QuestionContentResolver>();

        return services;
    }
}
