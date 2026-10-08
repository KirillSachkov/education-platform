using EducationContentService.Domain.ShortLinks;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ShortLinks.Queries;

/// <summary>
///     Резолв короткой ссылки: <c>GET /short-links/{code}/resolve</c> → 302 на страницу
///     материала <c>/knowledge-base/{materialId}</c> (relative Location). Анонимный и быстрый —
///     nginx проксирует сюда публичный <c>/s/{code}</c>. Entitlement не проверяется:
///     замок gated-контента отрабатывает на самой странице материала. Неизвестный код → 404.
/// </summary>
public sealed class ResolveShortLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("short-links/{code}/resolve", HandleAsync)
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> HandleAsync(
        [FromRoute] string code,
        [FromServices] IShortLinksRepository shortLinksRepository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(code) || code.Length > ShortLink.CODE_LENGTH * 2)
            return Results.NotFound();

        Result<ShortLink, Error> lookup = await shortLinksRepository.GetByAsync(
            l => l.Code == code, cancellationToken);

        if (lookup.IsFailure)
            return Results.NotFound();

        return Results.Redirect($"/knowledge-base/{lookup.Value.MaterialId}");
    }
}
