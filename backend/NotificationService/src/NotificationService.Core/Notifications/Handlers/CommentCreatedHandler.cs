using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using ContentAccess;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Comments.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>comment.events / comment.created</c> → до трёх нотификаций.
///
/// <list type="bullet">
/// <item><see cref="NotificationTemplates.CommentReplied"/> — автору parent-комментария, если это ответ
/// и автор ответа ≠ автор parent.</item>
/// <item><see cref="NotificationTemplates.CommentOnOwnContent"/> — владельцу сущности (автор курса/материала/задачи),
/// если он ≠ автор комментария и ≠ автор parent (чтобы один комментарий не генерировал два уведомления).</item>
/// <item><see cref="NotificationTemplates.CommentOnOwnContent"/> — создателю материала (помощнику-модератору),
/// который загрузил урок на курс другого автора (<c>materials.author_id ≠ автор курса</c>), #400. Шлётся только
/// для материалов и только если создатель ≠ владелец курса, ≠ автор комментария, ≠ получатель reply.</item>
/// </list>
///
/// Owner резолвится через <see cref="IEducationContentServiceClient.GetEntityOwnershipAsync"/>.
/// При любой ошибке enrichment — graceful fallback ("пользователь"): уведомление всё равно доходит.
///
/// Idempotency: <c>CorrelationId</c> на базе <c>CommentId</c> — retry не продублирует. Для обоих
/// получателей XOR с их userId, чтобы не было collision на unique-index (он сам включает recipient,
/// но оставляем старый паттерн fan-out из Broadcast / Material для единообразия).
/// </summary>
public sealed class CommentCreatedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<CommentCreatedHandler> _logger;

    public CommentCreatedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<CommentCreatedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(CommentCreated evt, CancellationToken ct)
    {
        // Параллельно: имя автора + ownership сущности. Оба cached.
        Task<string> authorNameTask = ResolveAuthorNameAsync(evt.AuthorId, ct);
        Task<EntityOwnershipDto?> ownershipTask = ResolveEntityOwnershipAsync(evt.EntityType, evt.EntityId, ct);
        await Task.WhenAll(authorNameTask, ownershipTask);

        string authorName = await authorNameTask;
        EntityOwnershipDto? ownership = await ownershipTask;
        Guid? ownerId = ownership?.AuthorId;
        Guid? courseId = ResolveRouteCourseId(evt.EntityType, evt.EntityId, ownership?.CourseId);
        CourseRouteContext? course = courseId is Guid routeCourseId
            ? await NotificationRouteContextResolver.ResolveCourseAsync(
                _ecsClient,
                _authClient,
                routeCourseId,
                _logger,
                ct)
            : null;

        List<NotificationRequest> requests = [];

        // --- 1. CommentReplied → автору parent'а ---
        if (evt.ParentAuthorId is Guid parentAuthorId
            && parentAuthorId != evt.AuthorId)
        {
            requests.Add(NotificationRequest.From(
                template: NotificationTemplates.CommentReplied,
                recipientUserId: parentAuthorId,
                correlationId: CorrelationIds.Combine(evt.CommentId, parentAuthorId),
                args: TemplateArgs.Of(
                    ("authorName", authorName),
                    ("preview", evt.Preview)),
                payload: new
                {
                    commentId = evt.CommentId,
                    entityType = evt.EntityType,
                    entityId = evt.EntityId,
                    courseId,
                    courseSlug = course?.CourseSlug,
                    authorSlug = course?.AuthorSlug,
                }));
        }

        // --- 2. CommentOnOwnContent → владельцу сущности, если он другой ---
        if (ownerId is Guid ownerUserId
            && ownerUserId != evt.AuthorId
            && ownerUserId != evt.ParentAuthorId)
        {
            requests.Add(NotificationRequest.From(
                template: NotificationTemplates.CommentOnOwnContent,
                recipientUserId: ownerUserId,
                correlationId: CorrelationIds.Combine(evt.CommentId, ownerUserId),
                args: TemplateArgs.Of(
                    ("authorName", authorName),
                    ("preview", evt.Preview)),
                payload: new
                {
                    commentId = evt.CommentId,
                    entityType = evt.EntityType,
                    entityId = evt.EntityId,
                    courseId,
                    courseSlug = course?.CourseSlug,
                    authorSlug = course?.AuthorSlug,
                }));
        }

        // --- 3. CommentOnOwnContent → создателю материала (помощнику), если он другой (#400) ---
        // Для материала ECS отдаёт CreatedByUserId = materials.author_id. Если урок загрузил
        // помощник (модератор) на курс другого автора, owner (ветка 2) = автор курса, а creator =
        // помощник. Уведомляем создателя дополнительно. Естественный дедуп: для материалов
        // владельца курса creator == owner → ветка 2 уже сработала, сюда не доходим.
        if (IsMaterial(evt.EntityType)
            && ownership?.CreatedByUserId is Guid creatorId
            && creatorId != ownerId
            && creatorId != evt.AuthorId
            && creatorId != evt.ParentAuthorId)
        {
            requests.Add(NotificationRequest.From(
                template: NotificationTemplates.CommentOnOwnContent,
                recipientUserId: creatorId,
                correlationId: CorrelationIds.Combine(evt.CommentId, creatorId),
                args: TemplateArgs.Of(
                    ("authorName", authorName),
                    ("preview", evt.Preview)),
                payload: new
                {
                    commentId = evt.CommentId,
                    entityType = evt.EntityType,
                    entityId = evt.EntityId,
                    courseId,
                    courseSlug = course?.CourseSlug,
                    authorSlug = course?.AuthorSlug,
                }));
        }

        if (requests.Count == 0)
        {
            _logger.LogDebug(
                "CommentCreated {CommentId} — no recipients (self-thread, unknown owner).",
                evt.CommentId);
            return;
        }

        await _dispatcher.DispatchAsync(requests, ct);
    }

    private static Guid? ResolveRouteCourseId(string entityType, Guid entityId, Guid? ownershipCourseId) =>
        string.Equals(entityType, "course", StringComparison.OrdinalIgnoreCase)
            ? entityId
            : ownershipCourseId;

    private static bool IsMaterial(string entityType) =>
        string.Equals(entityType, ResourceTypes.MATERIAL, StringComparison.OrdinalIgnoreCase);

    private async Task<string> ResolveAuthorNameAsync(Guid userId, CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
            await _authClient.GetUsersByIdsAsync([userId], ct);
        if (lookup.IsSuccess && lookup.Value is not null && lookup.Value.Count > 0)
        {
            AuthUserLookupDto user = lookup.Value[0];
            return user.Name ?? user.Username ?? "пользователь";
        }

        _logger.LogWarning(
            "Auth lookup failed for comment author {UserId}: {Error}. Using fallback.",
            userId, lookup.ErrorText());
        return "пользователь";
    }

    private async Task<EntityOwnershipDto?> ResolveEntityOwnershipAsync(string entityType, Guid entityId, CancellationToken ct)
    {
        // ECS возвращает (CourseId, AuthorId) для course/material/issue/module. CourseId нужен для
        // payload → PlatformLinkBuilder собирает /courses/{cid}/learn/{mid}?comment={cmt} link.
        // AuthorId нужен для фильтра CommentOnOwnContent-получателя.
        // При failure — graceful: payload.courseId = null (click редиректит на корень), owner-уведомление
        // пропускается (но reply-уведомление всё равно доходит).
        Result<EntityOwnershipDto, Error> lookup =
            await _ecsClient.GetEntityOwnershipAsync(entityType, entityId, ct);
        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value;

        _logger.LogWarning(
            "ECS ownership lookup failed for {Type}/{Id}: {Error}. CommentOnOwnContent skipped, payload.courseId=null.",
            entityType, entityId, lookup.ErrorText());
        return null;
    }
}
