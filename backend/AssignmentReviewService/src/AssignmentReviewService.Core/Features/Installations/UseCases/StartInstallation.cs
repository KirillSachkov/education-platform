using System.Security.Cryptography;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Vcs;
using Core.Abstractions;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Middleware;
using Shared.GitHubApp;
using GitHubAppOptions = AssignmentReviewService.Core.Features.Installations.Services.GitHubAppOptions;

namespace AssignmentReviewService.Core.Features.Installations.UseCases;

public sealed record StartInstallationRequest(string? ReturnUrl);

public sealed record StartInstallationResponse(string RedirectUrl, string State);

public sealed record StartInstallationCommand(string? ReturnUrl) : ICommand;

public sealed class StartInstallationValidator : AbstractValidator<StartInstallationCommand>
{
    public StartInstallationValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .MaximumLength(2048)
            .Must(BeRelativeOrEmpty)
            .WithMessage("ReturnUrl должен быть относительным путём (начинаться с '/').");
    }

    private static bool BeRelativeOrEmpty(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return true;
        // WHATWG URL parser treats backslashes as slashes for special URLs. Therefore
        // `/\\evil.example` is authority-like after browser normalization even though it
        // passes a naive single-slash check. Controls are rejected before Location header use.
        return returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.Contains('\\', StringComparison.Ordinal)
            && !returnUrl.Any(char.IsControl);
    }
}

public sealed class StartInstallationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/installations/start/",
                async Task<EndpointResult<StartInstallationResponse>> (
                    [FromBody] StartInstallationRequest request,
                    [FromServices] StartInstallationHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new StartInstallationCommand(request.ReturnUrl), ct))
            .RequireAuthorization()
            .RequireRateLimiting("ar-github-install-start");
    }
}

public sealed class StartInstallationHandler : ICommandHandler<StartInstallationResponse, StartInstallationCommand>
{
    // 30 мин (#451): на GitHub студент успевает создать/выбрать репозиторий и вернуться.
    // 10 мин слишком коротко — истёкший single-use state ронял install-callback, установка
    // не фиксировалась (webhook.created-recovery подхватывает такие, но меньше false-fail'ов —
    // лучше). Токен остаётся single-use + bound to UserId, CSRF-защита сохранена.
    internal static readonly TimeSpan STATE_TTL = TimeSpan.FromMinutes(30);

    private readonly IInstallStateStore<InstallStateData> _state;
    private readonly UserScopedData _user;
    private readonly GitHubAppOptions _options;
    private readonly IValidator<StartInstallationCommand> _validator;

    public StartInstallationHandler(
        IInstallStateStore<InstallStateData> state,
        UserScopedData user,
        IOptions<GitHubAppOptions> options,
        IValidator<StartInstallationCommand> validator)
    {
        _state = state;
        _user = user;
        _options = options.Value;
        _validator = validator;
    }

    public async Task<Result<StartInstallationResponse, Error>> Handle(
        StartInstallationCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
        {
            FluentValidation.Results.ValidationFailure first = validation.Errors[0];
            return Error.Validation("vcs.start_installation.invalid", first.ErrorMessage);
        }

        if (string.IsNullOrEmpty(_options.Slug))
        {
            return VcsErrors.InstallationTokenFailed(
                "AssignmentReview:GitHub:Slug not configured");
        }

        // 32 random bytes → URL-safe base64.
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        await _state.SetAsync(token, new InstallStateData(_user.UserId, command.ReturnUrl), STATE_TTL);

        string url = $"https://github.com/apps/{_options.Slug}/installations/new?state={token}";
        return new StartInstallationResponse(url, token);
    }
}
