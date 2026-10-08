using AccessService.Contracts.Users;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Users.UseCases;

/// <summary>
/// User lookup для author/admin'а, который выдаёт plan-grant вручную:
/// автор вводит имя/username/Telegram → видит подсказки → жмёт «Выдать».
/// Прокси через AuthService internal-endpoint, чтобы не выдавать
/// authors permission'у на полный admin user-search.
/// </summary>
public sealed class LookupUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/users/lookup/", async Task<EndpointResult<IReadOnlyList<UserLookupResultDto>>> (
                [FromQuery] string q,
                [FromQuery] int? limit,
                [FromServices] LookupUsersHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new LookupUsersQuery(q, limit ?? 10), ct))
            .RequirePermissions(PlatformPermissions.Plans.GRANT)
            .RequireRateLimiting("user-lookup");
}

public sealed record LookupUsersQuery(string Query, int Limit) : IQuery;

public sealed class LookupUsersQueryValidator : AbstractValidator<LookupUsersQuery>
{
    public LookupUsersQueryValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty()
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, InternalUsersSearchRequest.MAX_LIMIT)
            .WithError(GeneralErrors.ValueIsInvalid("limit"));
    }
}

public sealed class LookupUsersHandler
    : IQueryHandlerWithResult<IReadOnlyList<UserLookupResultDto>, LookupUsersQuery>
{
    private readonly IValidator<LookupUsersQuery> _validator;
    private readonly IAuthServiceClient _authClient;

    public LookupUsersHandler(
        IValidator<LookupUsersQuery> validator,
        IAuthServiceClient authClient)
    {
        _validator = validator;
        _authClient = authClient;
    }

    public async Task<Result<IReadOnlyList<UserLookupResultDto>, Error>> Handle(
        LookupUsersQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<IReadOnlyList<AuthUserLookupDto>, Error> result =
            await _authClient.SearchUsersAsync(query.Query, query.Limit, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error;
        }

        IReadOnlyList<UserLookupResultDto> dtos = result.Value
            .Select(u => new UserLookupResultDto(u.UserId, u.Name, u.Username, u.Email, u.AvatarId))
            .ToList();
        return Result.Success<IReadOnlyList<UserLookupResultDto>, Error>(dtos);
    }
}
