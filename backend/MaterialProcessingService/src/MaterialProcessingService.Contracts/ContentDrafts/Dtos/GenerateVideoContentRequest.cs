namespace MaterialProcessingService.Contracts.ContentDrafts.Dtos;

public sealed record GenerateVideoContentRequest(
    Guid MaterialId,
    /// <summary>
    ///     По умолчанию <c>false</c>: если у материала уже есть текст в Content,
    ///     enqueue вернёт 409 — это защита от молчаливой перезаписи руками
    ///     написанного автором markdown'а. Передать <c>true</c> чтобы обойти
    ///     (frontend показывает confirmation: «AI перезапишет ваш текст?»).
    /// </summary>
    bool ForceOverwrite = false,
    /// <summary>
    ///     Optional admin model override (например "openai/gpt-4.1-nano"). Игнорируется
    ///     если caller не admin. Persists на job → background handler использует.
    /// </summary>
    string? ModelOverride = null,
    /// <summary>
    ///     Admin-only override для requestedByUserId. Когда mcp-admin client_credentials
    ///     токен (sub=client_id → UserId=Guid.Empty) запускает bulk-операцию, ID указывают
    ///     явно. Игнорируется не-admin caller'ом.
    /// </summary>
    Guid? RequestedBy = null);
