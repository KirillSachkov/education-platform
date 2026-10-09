using System.Net;
using Core.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public class MarkMaterialViewedTests : ProgressServiceTestsBase
{
    public MarkMaterialViewedTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MarkMaterialViewed_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkMaterialViewed_ShouldCreateMaterialView_AndCascadeModuleItemProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId
                && x.ReferenceId == materialId
                && x.ItemType == ModuleItemProgressType.MATERIAL));
        ModuleProgress? moduleProgress = await ExecuteInDb(db =>
            db.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(view);
        Assert.True(view.IsCompleted);
        Assert.NotNull(view.CompletedAt);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.COMPLETED, moduleProgress.Status);
        Assert.Equal(1, moduleProgress.ItemsCompleted);
    }

    [Fact]
    public async Task MarkMaterialViewed_WithoutEnrollment_StillCreatesUserView_NoCascade()
    {
        // В новой user-scoped модели просмотр живёт вне enrollment'а: если юзер
        // не записан ни на один курс с этим материалом, MaterialView всё равно
        // создаётся (для сценариев space-level/public материалов), но каскада
        // на module_item_progress нет.
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        // Намеренно без AddMaterialCourseContext — orphan material.

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int moduleItemCount = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == materialId));

        Assert.NotNull(view);
        Assert.Equal(0, moduleItemCount);
    }

    [Fact]
    public async Task MarkMaterialViewed_Twice_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 2);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage first = await PostAsync($"/progress/materials/{materialId}/view");
        HttpResponseMessage second = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int moduleItemCount = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ReferenceId == materialId && x.ItemType == ModuleItemProgressType.MATERIAL));

        Assert.Equal(1, viewCount);
        Assert.Equal(1, moduleItemCount);
    }

    [Fact]
    public async Task CompleteMaterialView_ForExistingSilentTrack_ShouldUpgradeInSingleStatement()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");

        HttpResponseMessage trackResponse = await PostAsync($"/progress/materials/{materialId}/track-view");
        Assert.Equal(HttpStatusCode.OK, trackResponse.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IMaterialViewRepository repository = scope.ServiceProvider.GetRequiredService<IMaterialViewRepository>();
        ITransactionManager transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        var beginResult = await transactionManager.BeginTransactionAsync();
        Assert.True(beginResult.IsSuccess);

        var completeResult = await repository.CompleteAsync(userId, materialId);
        Assert.True(completeResult.IsSuccess);
        Assert.True(completeResult.Value.StateChanged);

        var commitResult = await transactionManager.CommitTransactionAsync();
        Assert.True(commitResult.IsSuccess);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));

        Assert.NotNull(view);
        Assert.True(view.IsCompleted);
        Assert.NotNull(view.CompletedAt);
    }

    [Fact]
    public async Task MarkMaterialViewed_MaterialInMultipleCourses_ShouldCompleteAllEnrollments()
    {
        // Cross-enrollment cascade: один просмотр материала двигает прогресс во всех
        // активных enrollment'ах пользователя, где содержится этот материал.
        Guid userId = Guid.NewGuid();
        Guid courseAId = Guid.NewGuid();
        Guid courseBId = Guid.NewGuid();
        Guid moduleAId = Guid.NewGuid();
        Guid moduleBId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseAId, hasFreeContent: true);
        EducationContentClient.AddCourse(courseBId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseAId, moduleAId, moduleItemsTotal: 1);
        EducationContentClient.AddMaterialCourseContext(materialId, courseBId, moduleBId, moduleItemsTotal: 1);

        await EnrollAsync(courseAId, userId);
        await EnrollAsync(courseBId, userId);

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int completedInA = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ModuleId == moduleAId
                && x.ReferenceId == materialId
                && x.Status == ModuleItemProgressStatus.COMPLETED));
        int completedInB = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ModuleId == moduleBId
                && x.ReferenceId == materialId
                && x.Status == ModuleItemProgressStatus.COMPLETED));

        // вне зависимости от числа затронутых enrollment'ов.

        Assert.Equal(1, viewCount);
        Assert.Equal(1, completedInA);
        Assert.Equal(1, completedInB);
    }

    [Fact]
    public async Task UnmarkMaterialViewed_AfterMark_ResetsCompletionButPreservesViewTrack()
    {
        // Issue #285: unmark больше НЕ удаляет строку — оставляем её как silent track
        // (is_completed=false), чтобы публичный счётчик «N просмотров» (#234) не «забывал»
        // о визите. Cascade на module_item_progress откатываем, как и раньше.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage markResponse = await PostAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, markResponse.StatusCode);

        HttpResponseMessage unmarkResponse =
            await AppHttpClient.DeleteAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, unmarkResponse.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId
                && x.ReferenceId == materialId
                && x.ItemType == ModuleItemProgressType.MATERIAL));
        ModuleProgress? moduleProgress = await ExecuteInDb(db =>
            db.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        // Запись сохранена, но is_completed сброшен — это silent track для счётчика.
        Assert.NotNull(view);
        Assert.False(view.IsCompleted);
        Assert.Null(view.CompletedAt);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.NOT_COMPLETED, moduleItemProgress.Status);
        Assert.Null(moduleItemProgress.CompletedAt);
        Assert.NotNull(moduleProgress);
        Assert.Equal(0, moduleProgress.ItemsCompleted);
        Assert.NotEqual(ModuleProgressStatus.COMPLETED, moduleProgress.Status);
    }

    [Fact]
    public async Task UnmarkMaterialViewed_WithoutPriorMark_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");

        HttpResponseMessage response =
            await AppHttpClient.DeleteAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));
        Assert.Equal(0, viewCount);
    }

    [Fact]
    public async Task UnmarkMaterialViewed_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();

        HttpResponseMessage response =
            await AppHttpClient.DeleteAsync($"/progress/materials/{materialId}/view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnmarkMaterialViewed_MaterialInMultipleCourses_ResetsAllEnrollments()
    {
        // Cross-enrollment cascade: unmark откатывает прогресс во всех активных
        // enrollment'ах пользователя, где содержится материал (зеркало mark-cascade).
        Guid userId = Guid.NewGuid();
        Guid courseAId = Guid.NewGuid();
        Guid courseBId = Guid.NewGuid();
        Guid moduleAId = Guid.NewGuid();
        Guid moduleBId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseAId, hasFreeContent: true);
        EducationContentClient.AddCourse(courseBId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseAId, moduleAId, moduleItemsTotal: 1);
        EducationContentClient.AddMaterialCourseContext(materialId, courseBId, moduleBId, moduleItemsTotal: 1);

        await EnrollAsync(courseAId, userId);
        await EnrollAsync(courseBId, userId);

        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);

        HttpResponseMessage unmarkResponse =
            await AppHttpClient.DeleteAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, unmarkResponse.StatusCode);

        // Issue #285: row сохраняется как silent track (is_completed=false), но cascade
        // в обоих enrollment'ах откатывается. Счётчик "N просмотров" не должен пострадать.
        MaterialView? remainingView = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int completedInA = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ModuleId == moduleAId
                && x.ReferenceId == materialId
                && x.Status == ModuleItemProgressStatus.COMPLETED));
        int completedInB = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ModuleId == moduleBId
                && x.ReferenceId == materialId
                && x.Status == ModuleItemProgressStatus.COMPLETED));

        Assert.NotNull(remainingView);
        Assert.False(remainingView.IsCompleted);
        Assert.Equal(0, completedInA);
        Assert.Equal(0, completedInB);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}