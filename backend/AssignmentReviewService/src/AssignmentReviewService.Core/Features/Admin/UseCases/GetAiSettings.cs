using AssignmentReviewService.Contracts.AiSettings;
using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Domain.AiSettings;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;

namespace AssignmentReviewService.Core.Features.Admin.UseCases;

public sealed record GetAiSettingsQuery : IQuery;

public sealed class GetAiSettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/assignment-review/admin/ai-settings/",
                async Task<EndpointResult<AssignmentReviewAiModelSettingsDto>> (
                    [FromServices] GetAiSettingsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetAiSettingsQuery(), ct))
            .RequirePermissions(PlatformPermissions.Platform.ADMIN);
    }
}

/// <summary>
///     Phase 11 (#15) — admin GET endpoint. Возвращает effective settings для
///     Reviewer slot'а с источником значений (<c>Source = CONFIG | DATABASE</c>)
///     + metadata о последнем обновлении.
/// </summary>
public sealed class GetAiSettingsHandler
    : IQueryHandlerWithResult<AssignmentReviewAiModelSettingsDto, GetAiSettingsQuery>
{
    private readonly IAiModelSettingsRepository _repository;
    private readonly IOptions<AssignmentReviewAiOptions> _options;

    public GetAiSettingsHandler(
        IAiModelSettingsRepository repository,
        IOptions<AssignmentReviewAiOptions> options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<Result<AssignmentReviewAiModelSettingsDto, Error>> Handle(
        GetAiSettingsQuery query, CancellationToken ct)
    {
        AiModelSettings? db = await _repository.GetSingletonAsync(ct);
        AssignmentReviewAiOptions cfg = _options.Value;

        return new AssignmentReviewAiModelSettingsDto(
            Reviewer: BuildSlot(db?.Reviewer, cfg.Reviewer),
            ReviewerBasePrompt: BuildBasePrompt(db?.ReviewerBasePrompt, cfg.ReviewerBasePrompt),
            ReviewEnabled: db?.ReviewEnabled ?? cfg.ReviewEnabled,
            RepoContextEnabled: db?.RepoContextEnabled ?? cfg.RepoContext.Enabled,
            UpdatedAt: db?.UpdatedAt,
            UpdatedByUserId: db?.UpdatedByUserId);
    }

    private static ReviewerBasePromptEffectiveDto BuildBasePrompt(string? db, string config)
    {
        return string.IsNullOrWhiteSpace(db)
            ? new ReviewerBasePromptEffectiveDto(config, AiModelSettingsSource.CONFIG.ToString())
            : new ReviewerBasePromptEffectiveDto(db, AiModelSettingsSource.DATABASE.ToString());
    }

    private static AiModelSlotEffectiveDto BuildSlot(AiModelSlot? db, AssignmentReviewAiSlot config)
    {
        if (db is not null)
        {
            return new AiModelSlotEffectiveDto(
                db.Model,
                db.Temperature,
                db.MaxOutputTokens,
                db.TimeoutSeconds,
                AiModelSettingsSource.DATABASE.ToString());
        }
        return new AiModelSlotEffectiveDto(
            config.Model,
            config.Temperature,
            config.MaxOutputTokens,
            config.TimeoutSeconds,
            AiModelSettingsSource.CONFIG.ToString());
    }
}
