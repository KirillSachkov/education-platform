using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.AuthorSpaces;
using AuthService.Domain.AuthorSpaces;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.AuthorSpaces.Queries;

public sealed record GetAuthorSpaceBySlugQuery(string Slug) : IQuery;

public sealed class GetAuthorSpaceBySlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/author-spaces/by-slug/{slug}", async Task<EndpointResult<AuthorSpacePublicResponse>> (
                    [FromRoute] string slug,
                    [FromServices] GetAuthorSpaceBySlugHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetAuthorSpaceBySlugQuery(slug), ct))
            .AllowAnonymousEndpoint();
}

public sealed class GetAuthorSpaceBySlugHandler
    : IQueryHandlerWithResult<AuthorSpacePublicResponse, GetAuthorSpaceBySlugQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<GetAuthorSpaceBySlugHandler> _logger;

    public GetAuthorSpaceBySlugHandler(
        ITransactionManager transactionManager,
        ILogger<GetAuthorSpaceBySlugHandler> logger)
    {
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task<Result<AuthorSpacePublicResponse, Error>> Handle(
        GetAuthorSpaceBySlugQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               s.id AS AuthorId,
                               s.slug AS Slug,
                               u.display_name AS DisplayName,
                               s.tagline AS Tagline,
                               s.logo_asset_id AS LogoAssetId,
                               s.feature_flags AS FeatureFlags,
                               up.avatar_id AS AvatarId,
                               up.role_profiles AS RoleProfiles
                           FROM author_spaces s
                           JOIN users u ON u.id = s.id
                           LEFT JOIN user_profiles up ON up.id = s.id
                           WHERE s.slug = @Slug
                           """;

        AuthorSpaceRow? row = await connection.QuerySingleOrDefaultAsync<AuthorSpaceRow>(
            sql,
            new { query.Slug });

        if (row is null)
            return AuthorSpaceErrors.NotFoundBySlug(query.Slug);

        string? specialization = null;
        string? aboutAsAuthor = null;

        if (row.RoleProfiles is not null)
        {
            specialization = row.RoleProfiles.Author?.Specialization;
            aboutAsAuthor = row.RoleProfiles.Author?.AboutAsAuthor;
        }

        AuthorSpaceFeatureFlagsDto flags = MapFlags(row.FeatureFlags);

        return new AuthorSpacePublicResponse(
            row.AuthorId,
            row.Slug,
            row.DisplayName,
            row.Tagline,
            specialization,
            aboutAsAuthor,
            row.AvatarId,
            row.LogoAssetId,
            flags);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly AuthorSpaceFeatureFlagsDto DefaultFlags = new(false, false, false, true, true, false);

    private AuthorSpaceFeatureFlagsDto MapFlags(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return DefaultFlags;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AuthorSpaceFeatureFlagsDto>(json, JsonOptions)
                   ?? DefaultFlags;
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deserialize AuthorSpace feature flags JSON, returning defaults");
            return DefaultFlags;
        }
    }

    private sealed record AuthorSpaceRow
    {
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string? DisplayName { get; init; }
        public string? Tagline { get; init; }
        public Guid? LogoAssetId { get; init; }
        public string? FeatureFlags { get; init; }
        public Guid? AvatarId { get; init; }
        public ProfilesDto? RoleProfiles { get; init; }
    }
}
