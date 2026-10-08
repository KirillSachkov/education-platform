namespace SearchService.Core.Reindex.State;

/// <summary>
/// Singleton-строка в <c>search.reindex_state</c>: отражает, какая версия
/// поисковой проекции последний раз успешно «прокатилась» в виде полного
/// reindex'а Typesense. Auto-reindex на старте сравнивает три сигнала с
/// конфигом/кодом: <see cref="AppliedGeneration"/> (ручной override),
/// <see cref="AppliedSchemaHash"/> (hash Typesense-схемы из кода) и
/// <see cref="AppliedDeployStamp"/> (версия релиза из CI).
/// </summary>
/// <remarks>
/// Таблица гарантированно содержит ровно одну строку с <c>id = 1</c>
/// (CHECK-constraint в миграции). Поэтому в коде используем
/// <see cref="SINGLETON_ID"/> и не строим API вокруг множественных entries.
/// </remarks>
public sealed class SearchReindexState
{
    public const int SINGLETON_ID = 1;

    private SearchReindexState()
    {
    }

    public int Id { get; private set; }

    public int AppliedGeneration { get; private set; }

    /// <summary>
    /// Hash схемы Typesense (<c>TypesenseSchemas</c>), с которой прошёл последний
    /// успешный полный реиндекс. <c>null</c> — реиндекс ещё ни разу не проходил
    /// после введения auto-инвалидации (трактуется как mismatch).
    /// </summary>
    public string? AppliedSchemaHash { get; private set; }

    /// <summary>
    /// Deploy-stamp (версия релиза / IMAGE_TAG из CI), с которым прошёл последний
    /// успешный полный реиндекс. <c>null</c> — стамп не задавался (dev/local).
    /// </summary>
    public string? AppliedDeployStamp { get; private set; }

    public DateTime? LastAppliedAtUtc { get; private set; }

    public Guid? LastRequestId { get; private set; }

    public static SearchReindexState Initial() => new()
    {
        Id = SINGLETON_ID,
        AppliedGeneration = 0,
        AppliedSchemaHash = null,
        AppliedDeployStamp = null,
        LastAppliedAtUtc = null,
        LastRequestId = null,
    };

    public void MarkApplied(
        int generation,
        string? schemaHash,
        string? deployStamp,
        Guid requestId,
        DateTime appliedAtUtc)
    {
        AppliedGeneration = generation;
        AppliedSchemaHash = schemaHash;
        AppliedDeployStamp = deployStamp;
        LastRequestId = requestId;
        LastAppliedAtUtc = appliedAtUtc;
    }
}
