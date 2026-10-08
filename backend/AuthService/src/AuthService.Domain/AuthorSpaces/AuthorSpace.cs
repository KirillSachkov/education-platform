using AuthService.Domain.ValueObjects;

namespace AuthService.Domain.AuthorSpaces;

/// <summary>Пространство автора на платформе. Id = Account.Id (1:1).</summary>
public sealed record AuthorSpace
{
    /// <summary>UUID автора (FK к Account.Id).</summary>
    public required Guid Id { get; init; }

    /// <summary>URL-безопасный слаг пространства (уникальный).</summary>
    public AuthorSpaceSlug Slug { get; set; } = null!;

    /// <summary>Короткое описание / подзаголовок пространства.</summary>
    public Tagline? Tagline { get; set; }

    /// <summary>ID файлового ассета логотипа пространства.</summary>
    public Guid? LogoAssetId { get; set; }

    /// <summary>Набор feature flags для данного пространства.</summary>
    public AuthorSpaceFeatureFlags FeatureFlags { get; set; } = new();

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; set; }

    public void Update(Tagline? tagline, Guid? logoAssetId, AuthorSpaceFeatureFlags? featureFlags, DateTime now)
    {
        Tagline = tagline;
        LogoAssetId = logoAssetId;

        if (featureFlags is not null)
            FeatureFlags = featureFlags;

        UpdatedAt = now;
    }

    public void UpdateSlug(AuthorSpaceSlug slug, DateTime now)
    {
        Slug = slug;
        UpdatedAt = now;
    }
}
