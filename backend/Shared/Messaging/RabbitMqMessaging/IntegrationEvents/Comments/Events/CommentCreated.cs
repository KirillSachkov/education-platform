namespace Shared.Messaging.IntegrationEvents.Comments.Events;

/// <summary>
/// Publish'ится CommentService после успешного создания комментария.
///
/// <para>
/// NotificationService consume'ит и диспатчит до двух нотификаций:
/// <list type="bullet">
/// <item><c>CommentReplied</c> — автору <see cref="ParentAuthorId"/> (если есть и != <see cref="AuthorId"/>).</item>
/// <item><c>CommentOnOwnContent</c> — владельцу сущности (<c>entityType</c>/<c>entityId</c>), если != <see cref="AuthorId"/> и != <see cref="ParentAuthorId"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="Preview"/> — первые ~280 символов контента (промо-превью для templates). Сервис публикующего
/// НЕ должен класть секретные данные; поле безопасно для отображения в InApp/Email/Telegram.
/// </para>
/// </summary>
/// <param name="CommentId">ID созданного комментария.</param>
/// <param name="AuthorId">ID автора комментария.</param>
/// <param name="EntityType">Тип сущности, к которой привязан комментарий (например, <c>"material"</c> / <c>"issue"</c>).</param>
/// <param name="EntityId">ID сущности.</param>
/// <param name="ParentId">ID parent-комментария (если это ответ) или <c>null</c> для root.</param>
/// <param name="ParentAuthorId">Автор parent-комментария — нужен handler'у для CommentReplied (если <paramref name="ParentId"/> не null).</param>
/// <param name="Preview">Короткая выдержка контента для превью (<c>≤ 280</c> символов).</param>
/// <param name="CreatedAt">Время создания (UTC) для сортировки/observability.</param>
public sealed record CommentCreated(
    Guid CommentId,
    Guid AuthorId,
    string EntityType,
    Guid EntityId,
    Guid? ParentId,
    Guid? ParentAuthorId,
    string Preview,
    DateTimeOffset CreatedAt);
