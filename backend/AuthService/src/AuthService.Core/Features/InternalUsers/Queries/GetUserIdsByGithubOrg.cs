using AuthService.Contracts;
using AuthService.Core.Database;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.InternalUsers.Queries;

public sealed record GetUserIdsByGithubOrgQuery(string OrgSlug) : IQuery;

public sealed class GetUserIdsByGithubOrgValidator : AbstractValidator<GetUserIdsByGithubOrgQuery>
{
    public GetUserIdsByGithubOrgValidator()
    {
        RuleFor(x => x.OrgSlug)
            .Must(slug => !string.IsNullOrWhiteSpace(slug))
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetUserIdsByGithubOrgQuery.OrgSlug)));

        RuleFor(x => x.OrgSlug)
            .MaximumLength(100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetUserIdsByGithubOrgQuery.OrgSlug)));
    }
}

public sealed class GetUserIdsByGithubOrgEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/by-github-org/{orgSlug}", async Task<EndpointResult<UserIdsByGithubOrgResponse>> (
                    string orgSlug,
                    GetUserIdsByGithubOrgHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserIdsByGithubOrgQuery(orgSlug), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetUserIdsByGithubOrgHandler
    : IQueryHandlerWithResult<UserIdsByGithubOrgResponse, GetUserIdsByGithubOrgQuery>
{
    private readonly IUserGithubOrgRepository _repository;
    private readonly IValidator<GetUserIdsByGithubOrgQuery> _validator;

    public GetUserIdsByGithubOrgHandler(
        IUserGithubOrgRepository repository,
        IValidator<GetUserIdsByGithubOrgQuery> validator)
    {
        _repository = repository;
        _validator = validator;
    }

    public async Task<Result<UserIdsByGithubOrgResponse, Error>> Handle(
        GetUserIdsByGithubOrgQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        IReadOnlyList<Guid> userIds = await _repository.GetUserIdsByOrgAsync(
            query.OrgSlug, cancellationToken);

        return new UserIdsByGithubOrgResponse(query.OrgSlug.ToLowerInvariant(), userIds);
    }
}
