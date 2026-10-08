using Core.Abstractions;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Features.Files;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Domain;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Features.AssetRegistry.Queries;

public sealed record GetDraftAssetsQuery(Guid DraftId) : IQuery;

public sealed class GetDraftAssetsQueryValidator : AbstractValidator<GetDraftAssetsQuery>
{
    public GetDraftAssetsQueryValidator()
    {
        RuleFor(x => x.DraftId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("draftId"));
    }
}

public sealed class GetDraftAssetsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/draft-assets", async Task<EndpointResult<List<GetFileResponse>>> (
                [FromQuery] Guid draftId,
                [FromServices] IQueryHandlerWithResult<List<GetFileResponse>, GetDraftAssetsQuery> handler,
                CancellationToken token) => await handler.Handle(new GetDraftAssetsQuery(draftId), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

public sealed class GetDraftAssetsHandler : IQueryHandlerWithResult<List<GetFileResponse>, GetDraftAssetsQuery>
{
    private readonly IMediaAssetRepository _repository;
    private readonly FileContentUrlBuilder _contentUrlBuilder;
    private readonly IValidator<GetDraftAssetsQuery> _validator;
    private readonly UserScopedData _user;

    public GetDraftAssetsHandler(
        IMediaAssetRepository repository,
        FileContentUrlBuilder contentUrlBuilder,
        IValidator<GetDraftAssetsQuery> validator,
        UserScopedData user)
    {
        _repository = repository;
        _contentUrlBuilder = contentUrlBuilder;
        _validator = validator;
        _user = user;
    }

    public async Task<Result<List<GetFileResponse>, Error>> Handle(
        GetDraftAssetsQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        List<MediaAsset> draftAssets = await _repository.GetActiveDraftAssetsAsync(query.DraftId, take: 100, cancellationToken);

        // Ownership gate: non-admin callers see only their own draft assets.
        // Drafts are per-user (random crypto.randomUUID()), so a leak across
        // authors is purely a defence-in-depth concern; still — return empty
        // if even one asset under the draft belongs to someone else.
        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && draftAssets.Any(a => a.UploadedByUserId != _user.UserId))
        {
            draftAssets = draftAssets.Where(a => a.UploadedByUserId == _user.UserId).ToList();
        }

        List<GetFileResponse> response = draftAssets
            .Select(a => a.ToFileResponse(_contentUrlBuilder.Build(a.Id)))
            .ToList();

        return response;
    }
}
