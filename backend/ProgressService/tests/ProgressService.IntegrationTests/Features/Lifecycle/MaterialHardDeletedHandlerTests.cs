using Common;
using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Bookmarks;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace ProgressService.IntegrationTests.Features.Lifecycle;

[Collection(nameof(IntegrationTestsFixture))]
public class MaterialHardDeletedHandlerTests : ProgressServiceTestsBase
{
    public MaterialHardDeletedHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MaterialHardDeleted_ShouldDeleteMaterialViews_ModuleItemProgress_AndBookmarks()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid otherMaterialId = Guid.NewGuid();

        Guid enrollmentId = await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);

            // User-scoped просмотр для удаляемого материала + «соседний» материал
            // для проверки, что его просмотр не затрагивается.
            MaterialView view = MaterialView.Create(userId, materialId).Value;
            MaterialView otherView = MaterialView.Create(userId, otherMaterialId).Value;
            MaterialView otherUserView = MaterialView.Create(otherUserId, materialId).Value;
            db.MaterialViews.Add(view);
            db.MaterialViews.Add(otherView);
            db.MaterialViews.Add(otherUserView);

            ModuleProgress moduleProgress = ModuleProgress.Create(enrollment.Id, moduleId, itemsTotal: 2).Value;
            db.ModuleProgresses.Add(moduleProgress);

            ModuleItemProgress item = ModuleItemProgress.CreateMaterialProgress(
                enrollment.Id, moduleId, materialId).Value;
            ModuleItemProgress otherItem = ModuleItemProgress.CreateMaterialProgress(
                enrollment.Id, moduleId, otherMaterialId).Value;
            db.ModuleItemProgresses.Add(item);
            db.ModuleItemProgresses.Add(otherItem);

            BookmarkEntityReference bookmarkRef = BookmarkEntityReference
                .Of(EntityType.Material, materialId).Value;
            MaterialBookmark bookmark = MaterialBookmark.Create(
                userId, courseId, bookmarkRef).Value;
            db.MaterialBookmarks.Add(bookmark);
            db.MaterialBookmarks.Add(MaterialBookmark.Create(userId, courseId,
                BookmarkEntityReference.Of(EntityType.Material, otherMaterialId).Value).Value);

            // Попытка квиза: квиз standalone (ST-13 #493) — удаление материала её НЕ трогает,
            // cleanup попыток идёт только через quiz.hard_deleted (QuizHardDeletedHandler).
            QuizAttemptAnswer answer = QuizAttemptAnswer.Create(
                Guid.NewGuid(), selectedOptionIds: [Guid.NewGuid()], textAnswer: null).Value;
            QuizAttempt attempt = QuizAttempt.Create(
                userId, Guid.NewGuid(), [answer], scorePercent: 50, passed: false).Value;
            db.QuizAttempts.Add(attempt);

            await db.SaveChangesAsync();
            return enrollment.Id;
        });

        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        // Views удалены только для удаляемого материала (и у user и у otherUser)
        int viewsForDeleted = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.MaterialId == materialId));
        Assert.Equal(0, viewsForDeleted);

        // Соседний view не тронут
        int otherMaterialViews = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.MaterialId == otherMaterialId));
        Assert.Equal(1, otherMaterialViews);

        // module_item_progress для удаляемого материала → удалён
        int moduleItemsForDeleted = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == materialId));
        Assert.Equal(0, moduleItemsForDeleted);

        // Соседний module_item_progress не тронут
        int otherModuleItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == otherMaterialId));
        Assert.Equal(1, otherModuleItems);

        // Закладки на материал удалены
        int bookmarksForDeleted = await ExecuteInDb(db =>
            db.MaterialBookmarks.CountAsync(x =>
                x.EntityReference.Type == EntityType.Material
                && x.EntityReference.Id == materialId));
        Assert.Equal(0, bookmarksForDeleted);
        Assert.Equal(1, await ExecuteInDb(db => db.MaterialBookmarks.CountAsync(x =>
            x.EntityReference.Type == EntityType.Material && x.EntityReference.Id == otherMaterialId)));

        // Попытки квизов переживают удаление материала: квиз standalone (ST-13 #493),
        // их cleanup — только на quiz.hard_deleted.
        int quizAttempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync());
        Assert.Equal(1, quizAttempts);
    }

    [Fact]
    public async Task MaterialHardDeleted_WithoutAnyMaterialData_ShouldBeNoOp()
    {
        Guid materialId = Guid.NewGuid();

        // Никаких записей нет — handler должен молча отработать.
        await InvokeMessageAndWaitAsync(new MaterialHardDeleted(materialId));

        int anyViews = await ExecuteInDb(db => db.MaterialViews.CountAsync());
        Assert.Equal(0, anyViews);
    }
}