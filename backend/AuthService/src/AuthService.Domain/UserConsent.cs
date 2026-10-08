using AuthService.Domain.ValueObjects;

namespace AuthService.Domain;

/// <summary>Факт согласия пользователя с конкретной версией юридического документа.
/// Юр-доказательство при споре по 152-ФЗ / ЗоЗПП. Хранится бессрочно.</summary>
public sealed record UserConsent
{
    /// <summary>UUID записи о согласии.</summary>
    public required Guid Id { get; init; }

    /// <summary>FK к Account.Id.</summary>
    public required Guid UserId { get; init; }

    /// <summary>Тип согласия.</summary>
    public required ConsentType ConsentType { get; init; }

    /// <summary>Версия документа, на которую дано согласие (например "v1").</summary>
    public required string DocumentVersion { get; init; }

    /// <summary>Момент предоставления согласия (UTC).</summary>
    public required DateTime AcceptedAt { get; init; }

    /// <summary>IP-адрес, с которого дано согласие.</summary>
    public required string IpAddress { get; init; }

    /// <summary>User-Agent браузера на момент согласия.</summary>
    public required string UserAgent { get; init; }
}
