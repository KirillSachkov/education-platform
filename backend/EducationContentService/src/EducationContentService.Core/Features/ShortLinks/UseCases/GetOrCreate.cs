using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.ShortLinks;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.ShortLinks;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ShortLinks.UseCases;

public sealed record GetOrCreateShortLinkCommand(Guid MaterialId) : ICommand;

public sealed class GetOrCreateShortLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("short-links/materials/{materialId:guid}", async Task<EndpointResult<ShortLinkDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetOrCreateShortLinkHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetOrCreateShortLinkCommand(materialId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW)
            .RequireRateLimiting("short-link-create");
    }
}

/// <summary>
///     Идемпотентный get-or-create короткой ссылки материала: существующая строка → тот же код.
///     Entitlement на тело материала НЕ требуется — шарить ссылку на gated-контент легитимно,
///     замок отработает на самой странице материала (`/knowledge-base/{id}`).
/// </summary>
public sealed class GetOrCreateShortLinkHandler : ICommandHandler<ShortLinkDto, GetOrCreateShortLinkCommand>
{
    private readonly IShortLinksRepository _shortLinksRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<GetOrCreateShortLinkHandler> _logger;

    public GetOrCreateShortLinkHandler(
        IShortLinksRepository shortLinksRepository,
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        ILogger<GetOrCreateShortLinkHandler> logger)
    {
        _shortLinksRepository = shortLinksRepository;
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task<Result<ShortLinkDto, Error>> Handle(
        GetOrCreateShortLinkCommand command,
        CancellationToken cancellationToken)
    {
        Result<ShortLink, Error> existing = await _shortLinksRepository.GetByAsync(
            l => l.MaterialId == command.MaterialId, cancellationToken);
        if (existing.IsSuccess)
            return new ShortLinkDto(existing.Value.Code);

        // Только PUBLISHED: иначе любой participant подтверждает существование чужих
        // DRAFT/ARCHIVED материалов перебором GUID'ов (existence oracle, ревью эпика #506).
        bool materialExists = await _materialsRepository.ExistsAsync(
            m => m.Id == command.MaterialId && m.Status == PublicationStatus.PUBLISHED,
            cancellationToken);
        if (!materialExists)
            return EducationErrors.MaterialNotFound(command.MaterialId);

        ShortLink shortLink = ShortLink.Create(command.MaterialId);
        await _shortLinksRepository.AddAsync(shortLink, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            // Гонка двух конкурентных POST'ов: unique-индекс по material_id отбил INSERT —
            // перечитываем код победителя (идемпотентный контракт сохраняется).
            Result<ShortLink, Error> winner = await _shortLinksRepository.GetByAsync(
                l => l.MaterialId == command.MaterialId, cancellationToken);
            if (winner.IsSuccess)
                return new ShortLinkDto(winner.Value.Code);

            return saveResult.Error;
        }

        _logger.LogInformation(
            "Short link {Code} created for material {MaterialId}",
            shortLink.Code, command.MaterialId);

        return new ShortLinkDto(shortLink.Code);
    }
}
