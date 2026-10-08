namespace NotificationService.Infrastructure.Postgres.Digest;

/// <summary>
/// Маркер «этот физический инбокс уже получил дайджест за данный проход». Чисто
/// инфраструктурная запись дедупа доставки — не доменный агрегат. Доступ — атомарный
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> через <see cref="DigestEmailDedupStore"/>;
/// EF знает о таблице только ради миграции/схемы.
/// </summary>
public sealed class DigestEmailDedupRecord
{
    /// <summary>
    /// Correlation прохода дайджеста (детерминированный guid от UTC-даты) — общий для всех
    /// писем одного прохода, что и делает его пригодным ключом дедупа по инбоксам.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>SHA-256 канонического инбокса (сам адрес в схему не пишем — PII).</summary>
#pragma warning disable CA1819 // SHA-256 hash is raw byte[] mapped to a bytea column
    public byte[] InboxHash { get; init; } = [];
#pragma warning restore CA1819

    public DateTime CreatedAt { get; init; }
}
