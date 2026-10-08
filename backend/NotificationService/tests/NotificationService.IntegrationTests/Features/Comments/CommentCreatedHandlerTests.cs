using AuthService.Contracts;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Ownership;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Comments.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.Comments;

/// <summary>
/// <c>CommentCreatedHandler</c> — диспатчит до трёх нотификаций на комментарий:
/// <c>CommentReplied</c> (автору parent'а), <c>CommentOnOwnContent</c> (владельцу сущности)
/// и <c>CommentOnOwnContent</c> создателю материала (если он ≠ автор курса — помощник, #400).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CommentCreatedHandlerTests : NotificationServiceTestsBase
{
    public CommentCreatedHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Reply_ToOtherUser_CreatesCommentRepliedForParentAuthor()
    {
        Guid parentAuthor = Guid.NewGuid();
        Guid replyAuthor = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(replyAuthor, "Alice");
        MockEcsOwnership(materialId, courseId: null, authorId: null);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: replyAuthor,
            EntityType: "material",
            EntityId: materialId,
            ParentId: Guid.NewGuid(),
            ParentAuthorId: parentAuthor,
            Preview: "Короткий текст ответа",
            CreatedAt: DateTimeOffset.UtcNow));

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == parentAuthor && n.Type == NotificationType.CommentReplied));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Reply_ToSelf_DoesNotNotifyParentAuthor()
    {
        // Автор отвечает на свой же комментарий — self-notification подавлена.
        Guid self = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(self, "Bob");
        MockEcsOwnership(materialId, courseId: null, authorId: null);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: self,
            EntityType: "material",
            EntityId: materialId,
            ParentId: Guid.NewGuid(),
            ParentAuthorId: self,
            Preview: "Ответ самому себе",
            CreatedAt: DateTimeOffset.UtcNow));

        int count = await ExecuteInDb(db => db.Notifications.CountAsync(n => n.RecipientUserId == self));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task RootComment_OnOwnedMaterial_CreatesCommentOnOwnContentForOwner()
    {
        Guid owner = Guid.NewGuid();
        Guid commenter = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(commenter, "Charlie");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: owner);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: commenter,
            EntityType: "material",
            EntityId: materialId,
            ParentId: null,
            ParentAuthorId: null,
            Preview: "Новый root-комментарий",
            CreatedAt: DateTimeOffset.UtcNow));

        int ownContent = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == owner && n.Type == NotificationType.CommentOnOwnContent));
        Assert.Equal(1, ownContent);

        // Автор комментария = commenter, owner != commenter, parent отсутствует → только одна нотификация.
        int total = await ExecuteInDb(db => db.Notifications.CountAsync());
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task ReplyToOtherOnOwnedMaterial_CreatesBothNotifications()
    {
        // Happy-path fan-out: A отвечает B на материал автора C.
        // Ожидаем 2 нотификации: CommentReplied → B, CommentOnOwnContent → C.
        Guid replyAuthor = Guid.NewGuid();
        Guid parentAuthor = Guid.NewGuid();
        Guid owner = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(replyAuthor, "Reply");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: owner);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: replyAuthor,
            EntityType: "material",
            EntityId: materialId,
            ParentId: Guid.NewGuid(),
            ParentAuthorId: parentAuthor,
            Preview: "Многосторонний ответ",
            CreatedAt: DateTimeOffset.UtcNow));

        int replied = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == parentAuthor && n.Type == NotificationType.CommentReplied));
        int ownContent = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == owner && n.Type == NotificationType.CommentOnOwnContent));
        int total = await ExecuteInDb(db => db.Notifications.CountAsync());

        Assert.Equal(1, replied);
        Assert.Equal(1, ownContent);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task OwnerCommentsOnOwnMaterial_NoNotifications()
    {
        // Автор курса сам пишет комментарий под свой же материал — не уведомляем никого.
        Guid owner = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(owner, "Dave");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: owner);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: owner,
            EntityType: "material",
            EntityId: materialId,
            ParentId: null,
            ParentAuthorId: null,
            Preview: "Авторский комментарий",
            CreatedAt: DateTimeOffset.UtcNow));

        int total = await ExecuteInDb(db => db.Notifications.CountAsync());
        Assert.Equal(0, total);
    }

    private void MockAuthReturnsName(Guid userId, string name)
    {
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(userId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(UserId: userId, Name: name, Username: name, Email: $"{userId}@test", AvatarId: null)]));
    }

    [Fact]
    public async Task RootComment_OnMaterialUploadedByAssistant_NotifiesBothOwnerAndCreator()
    {
        // Помощник (creator) загрузил урок на курс автора (owner). Студент (commenter) комментирует.
        // Ожидаем 2 CommentOnOwnContent: автору курса + создателю-помощнику (#400).
        Guid courseAuthor = Guid.NewGuid();
        Guid assistantCreator = Guid.NewGuid();
        Guid commenter = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(commenter, "Student");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: courseAuthor, createdByUserId: assistantCreator);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: commenter,
            EntityType: "material",
            EntityId: materialId,
            ParentId: null,
            ParentAuthorId: null,
            Preview: "Комментарий к уроку помощника",
            CreatedAt: DateTimeOffset.UtcNow));

        int ownerNotifs = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == courseAuthor && n.Type == NotificationType.CommentOnOwnContent));
        int creatorNotifs = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == assistantCreator && n.Type == NotificationType.CommentOnOwnContent));
        int total = await ExecuteInDb(db => db.Notifications.CountAsync());

        Assert.Equal(1, ownerNotifs);
        Assert.Equal(1, creatorNotifs);
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task RootComment_OnMaterialCreatedByCourseAuthor_OnlyOneNotification()
    {
        // Урок создан самим автором курса → creator == owner. Дедуп: ровно одна нотификация,
        // не дублируем владельца как «создателя» (#400).
        Guid courseAuthor = Guid.NewGuid();
        Guid commenter = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(commenter, "Student");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: courseAuthor, createdByUserId: courseAuthor);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: commenter,
            EntityType: "material",
            EntityId: materialId,
            ParentId: null,
            ParentAuthorId: null,
            Preview: "Комментарий к авторскому уроку",
            CreatedAt: DateTimeOffset.UtcNow));

        int ownerNotifs = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == courseAuthor && n.Type == NotificationType.CommentOnOwnContent));
        int total = await ExecuteInDb(db => db.Notifications.CountAsync());

        Assert.Equal(1, ownerNotifs);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task RootComment_CreatorIsCommenter_DoesNotNotifyCreator()
    {
        // Помощник-создатель сам комментирует свой урок на чужом курсе → создателю не шлём
        // (creator == автор коммента). Автор курса получает свою нотификацию (#400).
        Guid courseAuthor = Guid.NewGuid();
        Guid assistantCreator = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        MockAuthReturnsName(assistantCreator, "Assistant");
        MockEcsOwnership(materialId, courseId: Guid.NewGuid(), authorId: courseAuthor, createdByUserId: assistantCreator);

        await InvokeMessageAndWaitAsync(new CommentCreated(
            CommentId: Guid.NewGuid(),
            AuthorId: assistantCreator,
            EntityType: "material",
            EntityId: materialId,
            ParentId: null,
            ParentAuthorId: null,
            Preview: "Помощник комментирует свой урок",
            CreatedAt: DateTimeOffset.UtcNow));

        int creatorNotifs = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == assistantCreator));
        int ownerNotifs = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == courseAuthor && n.Type == NotificationType.CommentOnOwnContent));
        int total = await ExecuteInDb(db => db.Notifications.CountAsync());

        Assert.Equal(0, creatorNotifs);
        Assert.Equal(1, ownerNotifs);
        Assert.Equal(1, total);
    }

    private void MockEcsOwnership(Guid entityId, Guid? courseId, Guid? authorId, Guid? createdByUserId = null)
    {
        EducationContentClient.GetEntityOwnershipAsync(
                Arg.Any<string>(),
                entityId,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(new EntityOwnershipDto(courseId, authorId, createdByUserId)));
    }
}
