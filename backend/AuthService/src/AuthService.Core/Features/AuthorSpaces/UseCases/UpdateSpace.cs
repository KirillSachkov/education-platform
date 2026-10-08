using AuthService.Contracts.AuthorSpaces;
using AuthService.Core.Database;
using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.AuthorSpaces.UseCases;

public sealed record UpdateAuthorSpaceCommand(UpdateAuthorSpaceRequest Request) : ICommand;

public sealed class UpdateAuthorSpaceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/author-space", async Task<EndpointResult<Guid>> (
                    [FromBody] UpdateAuthorSpaceRequest request,
                    [FromServices] UpdateAuthorSpaceHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateAuthorSpaceCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Profiles.AUTHOR);
}

public sealed class UpdateAuthorSpaceValidator : AbstractValidator<UpdateAuthorSpaceCommand>
{
    public UpdateAuthorSpaceValidator()
    {
        RuleFor(x => x.Request.Tagline)
            .MustBeValueObject(value => Tagline.Create(value!))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Tagline));
    }
}

public sealed class UpdateAuthorSpaceHandler : ICommandHandler<Guid, UpdateAuthorSpaceCommand>
{
    private readonly IAuthorSpaceRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateAuthorSpaceCommand> _validator;

    public UpdateAuthorSpaceHandler(
        IAuthorSpaceRepository repository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateAuthorSpaceCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateAuthorSpaceCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        AuthorSpace? space = await _repository.GetByAsync(
            s => s.Id == _user.UserId, cancellationToken);
        if (space is null)
            return AuthorSpaceErrors.NotFound(_user.UserId);

        Tagline? tagline = Tagline.CreateOptional(command.Request.Tagline).Value;

        AuthorSpaceFeatureFlags? flags = command.Request.FeatureFlags is { } dto
            ? new AuthorSpaceFeatureFlags
            {
                GitHubIntegration = dto.GitHubIntegration,
                PrReviews = dto.PrReviews,
                AiAssistant = dto.AiAssistant,
                Roadmaps = dto.Roadmaps,
                Leaderboard = dto.Leaderboard,
                CustomLanding = dto.CustomLanding,
            }
            : null;

        space.Update(tagline, command.Request.LogoAssetId, flags, DateTime.UtcNow);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return space.Id;
    }
}
