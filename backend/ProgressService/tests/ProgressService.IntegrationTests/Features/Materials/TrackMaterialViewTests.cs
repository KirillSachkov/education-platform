using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public class TrackMaterialViewTests : ProgressServiceTestsBase
{
    public TrackMaterialViewTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task TrackMaterialView_WithoutAuth_ShouldReturn401()
    {
        // /track-view не помечен AllowAnonymous: эту точку дёргает <c>useTrackMaterialView</c>
        // только для авторизованных. Анонимы идут через <c>POST /anonymous-view</c>.
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/track-view");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TrackMaterialView_ShouldCreateSilentTrack_NoCascade_NoXp()
    {
        // Основная цель issue #285: silent track НЕ должен ни каскадить module_item_progress,
        // ни начислять XP. Только пишет строку в material_views с is_completed=false для
        // публичного счётчика «N просмотров».
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage response = await PostAsync($"/progress/materials/{materialId}/track-view");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int completedItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ReferenceId == materialId && x.Status == ModuleItemProgressStatus.COMPLETED));
        int xpAwardCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x =>
                x.UserId == userId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));

        Assert.NotNull(view);
        Assert.False(view.IsCompleted);
        Assert.Null(view.CompletedAt);
        // Никакого «изучено» в курсе — это ключевое требование issue #285.
        Assert.Equal(0, completedItems);
        Assert.Equal(0, xpAwardCount);
    }

    [Fact]
    public async Task TrackMaterialView_Twice_ShouldBeIdempotent()
    {
        // ON CONFLICT DO NOTHING — повторный track не плодит строки.
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");

        HttpResponseMessage first = await PostAsync($"/progress/materials/{materialId}/track-view");
        HttpResponseMessage second = await PostAsync($"/progress/materials/{materialId}/track-view");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));

        Assert.Equal(1, viewCount);
    }

    [Fact]
    public async Task TrackMaterialView_AfterMark_ShouldNotDowngradeCompletion()
    {
        // Defensive: silent track не должен ПОНИЗИТЬ существующий явный mark до «не изучено».
        // На UI этот сценарий редкий (track-view фактически выстреливается на mount, ДО
        // того как пользователь нажмёт кнопку), но прямой API-вызов или race могут
        // получиться. ON CONFLICT DO NOTHING защищает от downgrade.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        // Сначала explicit mark — пишет is_completed=true и каскадит.
        HttpResponseMessage markResponse = await PostAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, markResponse.StatusCode);

        // Затем silent track — должен быть no-op (запись уже есть).
        HttpResponseMessage trackResponse = await PostAsync($"/progress/materials/{materialId}/track-view");
        Assert.Equal(HttpStatusCode.OK, trackResponse.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId
                && x.ReferenceId == materialId
                && x.ItemType == ModuleItemProgressType.MATERIAL));

        Assert.NotNull(view);
        Assert.True(view.IsCompleted);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
    }

    [Fact]
    public async Task MarkAfterTrack_ShouldUpgradeToCompleted_AndCascade()
    {
        // Пользователь зашёл на страницу (silent track) → потом нажал «Отметить изученным».
        // Запись должна быть «дозревшей» до is_completed=true, cascade на module_item_progress
        // должен сработать ровно один раз (на upgrade), XP начислен ровно один раз.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        // 1) Silent track — никакого cascade.
        HttpResponseMessage trackResponse = await PostAsync($"/progress/materials/{materialId}/track-view");
        Assert.Equal(HttpStatusCode.OK, trackResponse.StatusCode);

        // 2) Explicit mark — апгрейдит is_completed и каскадит.
        HttpResponseMessage markResponse = await PostAsync($"/progress/materials/{materialId}/view");
        Assert.Equal(HttpStatusCode.OK, markResponse.StatusCode);

        MaterialView? view = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId && x.ReferenceId == materialId));
        int xpAwardCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x =>
                x.UserId == userId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));

        // Строка одна (track + upgrade), но теперь completed.
        Assert.Equal(1, viewCount);
        Assert.NotNull(view);
        Assert.True(view.IsCompleted);
        Assert.NotNull(view.CompletedAt);
        // Cascade сработал.
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
        // XP начислен один раз — даже после двойного цикла track→mark.
        Assert.Equal(1, xpAwardCount);
    }

    [Fact]
    public async Task TrackThenMarkThenTrack_ShouldNotDuplicateXp_NorCascadeAgain()
    {
        // Idempotency на каждом шаге: track → mark → track — XP и module_item_progress
        // не должны меняться после первого mark.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        await EnrollAsync(courseId, userId);

        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/track-view")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/track-view")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == userId && x.MaterialId == materialId));
        int xpAwardCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x =>
                x.UserId == userId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));

        Assert.Equal(1, viewCount);
        Assert.Equal(1, xpAwardCount);
    }

    [Fact]
    public async Task TrackMaterialView_StillCountsInPublicViewsCounter()
    {
        // Issue #234: счётчик «N просмотров» питается из (material_views ∪ anonymous_material_views).
        // Silent track-view auth-юзера обязан попадать в counter — иначе фича сломалась.
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");

        HttpResponseMessage trackResponse = await PostAsync($"/progress/materials/{materialId}/track-view");
        Assert.Equal(HttpStatusCode.OK, trackResponse.StatusCode);

        HttpResponseMessage countResponse = await PostAsJsonAsync(
            "/progress/materials/views/counts",
            new GetMaterialViewsCountsRequest(new[] { materialId }));
        Assert.Equal(HttpStatusCode.OK, countResponse.StatusCode);

        Envelope<GetMaterialViewsCountsResponse>? envelope =
            await countResponse.Content.ReadFromJsonAsync<Envelope<GetMaterialViewsCountsResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        MaterialViewsCountDto? item = envelope.Result.Items.FirstOrDefault(x => x.MaterialId == materialId);
        Assert.NotNull(item);
        Assert.Equal(1L, item.Count);
    }

    [Fact]
    public async Task TrackMaterialView_ShouldNotMarkAsCompletedInLearningState()
    {
        // Issue #285 — основная регрессия: после mount detail-страницы материал НЕ должен
        // появляться в "completed materials" для course learning state.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Test course",
            "desc",
            totalModules: 1,
            totalMaterials: 1,
            materialIds: new[] { materialId });

        await EnrollAsync(courseId, userId);

        Assert.Equal(HttpStatusCode.OK,
            (await PostAsync($"/progress/materials/{materialId}/track-view")).StatusCode);

        HttpResponseMessage statusResponse = await PostAsJsonAsync(
            "/progress/materials/view-status",
            new GetMaterialViewStatusRequest(new[] { materialId }));
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        Envelope<GetMaterialViewStatusResponse>? envelope =
            await statusResponse.Content.ReadFromJsonAsync<Envelope<GetMaterialViewStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        MaterialViewStatusDto? statusItem = envelope.Result.Items.FirstOrDefault(x => x.MaterialId == materialId);
        Assert.NotNull(statusItem);
        Assert.False(statusItem.IsViewed);
        Assert.Null(statusItem.ViewedAt);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}
