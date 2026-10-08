using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Core.Abstractions;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.GitHubOrgs;

/// <summary>
///     Утилита для проверки существования GitHub-организации через публичный GitHub API.
///     Используется UI plan-edit-form'ы для live-валидации `plan.github_org_slug`.
///     Endpoint размещён под `courses/...` префиксом, чтобы не править nginx routing —
///     это утилита, не привязанная к доменной модели курсов.
/// </summary>
public sealed record GitHubOrgInfoDto(string Login, string Name, string AvatarUrl);

public sealed record ValidateGitHubOrgQuery(string Slug) : IQuery;

public sealed class ValidateGitHubOrgQueryValidator : AbstractValidator<ValidateGitHubOrgQuery>
{
    public ValidateGitHubOrgQueryValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithMessage("Slug обязателен")
            .MaximumLength(100)
            .Matches("^[a-zA-Z0-9-]+$")
            .WithMessage("Допустимы только буквы, цифры и дефисы");
    }
}

public sealed class ValidateGitHubOrgEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/github-orgs/{slug}/validate", async Task<EndpointResult<GitHubOrgInfoDto>> (
                [FromRoute] string slug,
                [FromServices] ValidateGitHubOrgHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new ValidateGitHubOrgQuery(slug), cancellationToken))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed class ValidateGitHubOrgHandler : IQueryHandlerWithResult<GitHubOrgInfoDto, ValidateGitHubOrgQuery>
{
    private static readonly TimeSpan CACHE_DURATION = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly IValidator<ValidateGitHubOrgQuery> _validator;

    public ValidateGitHubOrgHandler(
        IHttpClientFactory httpClientFactory,
        IMemoryCache memoryCache,
        IValidator<ValidateGitHubOrgQuery> validator)
    {
        _httpClient = httpClientFactory.CreateClient("GitHubApi");
        _memoryCache = memoryCache;
        _validator = validator;
    }

    public async Task<Result<GitHubOrgInfoDto, Error>> Handle(
        ValidateGitHubOrgQuery query, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return Error.Validation("github.org.invalid.slug", validationResult.Errors[0].ErrorMessage);

        string cacheKey = $"github-org:{query.Slug.ToLowerInvariant()}";

        if (_memoryCache.TryGetValue(cacheKey, out GitHubOrgInfoDto? cached) && cached is not null)
            return cached;

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"orgs/{query.Slug}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Error.NotFound("github.org.not.found", $"GitHub организация «{query.Slug}» не найдена");

            response.EnsureSuccessStatusCode();

            GitHubOrgResponse? org = await response.Content.ReadFromJsonAsync<GitHubOrgResponse>(cancellationToken);
            if (org is null)
                return Error.NotFound("github.org.not.found", $"GitHub организация «{query.Slug}» не найдена");

            var dto = new GitHubOrgInfoDto(org.Login, org.Name ?? org.Login, org.AvatarUrl);

            _memoryCache.Set(cacheKey, dto, CACHE_DURATION);

            return dto;
        }
        catch (HttpRequestException)
        {
            return Error.Failure("github.org.api.error", "Не удалось связаться с GitHub API");
        }
        catch (TaskCanceledException)
        {
            return Error.Failure("github.org.api.timeout", "Превышено время ожидания ответа GitHub API");
        }
    }

    private sealed record GitHubOrgResponse(
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("avatar_url")] string AvatarUrl);
}
