using AuthService.Contracts;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Domain;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.InternalUsers.Queries;

/// <summary>
///     Резолвит платформенного юзера по external id его GitHub-аккаунта (numeric
///     GitHub user id). Источник — Identity external logins (<c>user_logins</c>,
///     провайдер <c>GitHub</c>, <c>provider_key</c> = external id). Internal-only.
///     Используется AssignmentReviewService для webhook-recovery установки GitHub App
///     (#451): когда install-callback не отработал (истёкший state-token и т.п.),
///     webhook <c>installation.created</c> резолвит владельца установки через этот
///     эндпоинт и создаёт <c>VcsInstallation</c>, привязанный к нужному юзеру.
/// </summary>
public sealed record GetUserIdByGithubExternalIdQuery(string ExternalId) : IQuery;

public sealed class GetUserIdByGithubExternalIdValidator : AbstractValidator<GetUserIdByGithubExternalIdQuery>
{
    public GetUserIdByGithubExternalIdValidator()
    {
        RuleFor(x => x.ExternalId)
            .Must(externalId => !string.IsNullOrWhiteSpace(externalId))
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetUserIdByGithubExternalIdQuery.ExternalId)));

        RuleFor(x => x.ExternalId)
            .MaximumLength(100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetUserIdByGithubExternalIdQuery.ExternalId)));
    }
}

public sealed class GetUserIdByGithubExternalIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/by-github-id/{externalId}", async Task<EndpointResult<UserIdByGithubIdResponse>> (
                    string externalId,
                    GetUserIdByGithubExternalIdHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserIdByGithubExternalIdQuery(externalId), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetUserIdByGithubExternalIdHandler
    : IQueryHandlerWithResult<UserIdByGithubIdResponse, GetUserIdByGithubExternalIdQuery>
{
    private readonly UserManager<Account> _userManager;
    private readonly IValidator<GetUserIdByGithubExternalIdQuery> _validator;

    public GetUserIdByGithubExternalIdHandler(
        UserManager<Account> userManager,
        IValidator<GetUserIdByGithubExternalIdQuery> validator)
    {
        _userManager = userManager;
        _validator = validator;
    }

    public async Task<Result<UserIdByGithubIdResponse, Error>> Handle(
        GetUserIdByGithubExternalIdQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // FindByLoginAsync читает auth.user_logins по (login_provider, provider_key).
        // Тот же провайдер ("GitHub") и тот же external id, что пишет GitHubLinkHandler.
        Account? account = await _userManager.FindByLoginAsync(
            GitHubRoutes.PROVIDER_NAME, query.ExternalId);

        // 200 с UserId=null когда привязки нет — caller (ARS) трактует это как
        // «установка на GitHub-аккаунте, не привязанном к платформе» и пропускает
        // recovery, а не как ошибку.
        return new UserIdByGithubIdResponse(query.ExternalId, account?.Id);
    }
}
