using AuthService.Contracts;
using AuthService.Domain;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Users.Queries;

public sealed record CheckUsernameAvailabilityQuery(string Username) : IQuery;

public sealed class CheckUsernameAvailabilityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/check-username", async Task<EndpointResult<CheckUsernameAvailabilityResponse>> (
                    [Microsoft.AspNetCore.Http.AsParameters] CheckUsernameAvailabilityRequest request,
                    [Microsoft.AspNetCore.Mvc.FromServices] CheckUsernameAvailabilityHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new CheckUsernameAvailabilityQuery(request.Username), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed record CheckUsernameAvailabilityRequest(string Username);

public sealed class CheckUsernameAvailabilityQueryValidator : AbstractValidator<CheckUsernameAvailabilityQuery>
{
    public CheckUsernameAvailabilityQueryValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("username"))
            .MinimumLength(3).WithError(GeneralErrors.ValueIsInvalid("username"))
            .MaximumLength(30).WithError(GeneralErrors.ValueIsInvalid("username"))
            .Matches(@"^[a-zA-Z0-9_\-.]+$").WithError(GeneralErrors.ValueIsInvalid("username"));
    }
}

public sealed class CheckUsernameAvailabilityHandler
    : IQueryHandlerWithResult<CheckUsernameAvailabilityResponse, CheckUsernameAvailabilityQuery>
{
    private readonly UserManager<Account> _userManager;
    private readonly IValidator<CheckUsernameAvailabilityQuery> _validator;

    public CheckUsernameAvailabilityHandler(
        UserManager<Account> userManager,
        IValidator<CheckUsernameAvailabilityQuery> validator)
    {
        _userManager = userManager;
        _validator = validator;
    }

    public async Task<Result<CheckUsernameAvailabilityResponse, Error>> Handle(
        CheckUsernameAvailabilityQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? existing = await _userManager.FindByNameAsync(query.Username);
        return new CheckUsernameAvailabilityResponse(IsAvailable: existing is null);
    }
}
