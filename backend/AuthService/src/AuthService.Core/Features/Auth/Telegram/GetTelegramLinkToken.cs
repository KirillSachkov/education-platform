using System.Globalization;
using AuthService.Contracts;
using AuthService.Core.Options;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Auth.Telegram;

public sealed record GetTelegramLinkTokenQuery : IQuery;

public sealed class GetTelegramLinkTokenEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/me/telegram/link-token", async Task<EndpointResult<GetTelegramLinkTokenResponse>> (
                    [FromServices] GetTelegramLinkTokenHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetTelegramLinkTokenQuery(), ct))
            .RequireAuthorization()
            .RequireRateLimiting("telegram-link");
}

public sealed class GetTelegramLinkTokenHandler
    : IQueryHandlerWithResult<GetTelegramLinkTokenResponse, GetTelegramLinkTokenQuery>
{
    private readonly ITelegramLinkTokenStore _tokenStore;
    private readonly UserScopedData _user;
    private readonly TelegramLinkOptions _options;
    private readonly ILogger<AuthAudit> _audit;

    public GetTelegramLinkTokenHandler(
        ITelegramLinkTokenStore tokenStore,
        UserScopedData user,
        IOptions<TelegramLinkOptions> options,
        ILogger<AuthAudit> audit)
    {
        _tokenStore = tokenStore;
        _user = user;
        _options = options.Value;
        _audit = audit;
    }

    public async Task<Result<GetTelegramLinkTokenResponse, Error>> Handle(
        GetTelegramLinkTokenQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotUsername))
            return AuthErrors.TelegramLinkNotConfigured();

        string? token = await _tokenStore.GenerateAsync(_user.UserId);
        if (string.IsNullOrEmpty(token))
            return AuthErrors.TelegramLinkTokenGenerationFailed();

        string deepLink = string.Create(
            CultureInfo.InvariantCulture,
            $"https://t.me/{_options.BotUsername}?start={token}");

        _audit.LogTelegramLinkTokenIssued(_user.UserId);

        return new GetTelegramLinkTokenResponse(token, _options.BotUsername, deepLink);
    }
}
