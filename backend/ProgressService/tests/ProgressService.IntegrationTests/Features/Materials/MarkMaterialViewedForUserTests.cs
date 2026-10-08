using System.Net;
using ContentAccess;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Materials;

[Collection(nameof(IntegrationTestsFixture))]
public class MarkMaterialViewedForUserTests : ProgressServiceTestsBase
{
    public MarkMaterialViewedForUserTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MarkForUser_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid materialId = Guid.NewGuid();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkForUser_AsParticipant_ShouldReturn403()
    {
        // platform-participant не имеет Progress.MANAGE → Tier-1 отсекает на гейте.
        Guid actorId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(actorId, "platform-participant");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkForUser_TargetWithAccess_CreatesCompletedView_AndCascades_AndAwardsXpToTarget()
    {
        // Scenario (a): admin отмечает материал target-юзеру с доступом → 200, target получает
        // material_view(is_completed=true) + cascade в module_item_progress + MATERIAL_VIEWED XP.
        Guid actorAdminId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(actorAdminId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        // target имеет progress-anchor (как реальный записанный студент).
        await SeedEnrollmentAsync(courseId, targetUserId);

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(targetUserId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // material_view создан для ЦЕЛЕВОГО юзера, не для actor'а.
        MaterialView? targetView = await ExecuteInDb(db =>
            db.MaterialViews.FirstOrDefaultAsync(x => x.UserId == targetUserId && x.MaterialId == materialId));
        int actorViewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == actorAdminId && x.MaterialId == materialId));

        // Cascade на module_item_progress в enrollment'е target'а (доказывает, что MaterialViewedEvent
        // поднялся с target-UserId — у ProgressService нет OutboxCollector'а, событие domain-уровня,
        // поэтому проверяем его эффекты: cascade + XP-строку).
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ModuleId == moduleId
                && x.ReferenceId == materialId
                && x.ItemType == ModuleItemProgressType.MATERIAL));

        // XP начислен ЦЕЛЕВОМУ юзеру (доказательство MaterialViewedEvent.UserId == target).
        XpAward? targetAward = await ExecuteInDb(db =>
            db.XpAwards.FirstOrDefaultAsync(x =>
                x.UserId == targetUserId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));
        int actorAwardCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x => x.UserId == actorAdminId && x.AwardType == XpAwardType.MATERIAL_VIEWED));

        Assert.NotNull(targetView);
        Assert.True(targetView.IsCompleted);
        Assert.NotNull(targetView.CompletedAt);
        Assert.Equal(0, actorViewCount);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
        Assert.NotNull(targetAward);
        Assert.Null(targetAward.EnrollmentId);
        Assert.Equal(0, actorAwardCount);
    }

    [Fact]
    public async Task MarkForUser_TargetWithoutAccess_ShouldReturn403_AndCreateNoRow()
    {
        // Scenario (b): target БЕЗ доступа к материалу → 403, material_view не создаётся
        // (иначе phantom progress). DenyAll глобально не подходит (admin actor мог бы пройти),
        // поэтому переопределяем решение per-resource — target (non-admin subject) получит Denied.
        Guid actorAdminId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(actorAdminId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);

        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, materialId, AccessDecision.Denied());

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(targetUserId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == targetUserId && x.MaterialId == materialId));
        int moduleItemCount = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == materialId));
        int xpCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x => x.UserId == targetUserId && x.SourceId == materialId));

        Assert.Equal(0, viewCount);
        Assert.Equal(0, moduleItemCount);
        Assert.Equal(0, xpCount);
    }

    [Fact]
    public async Task MarkForUser_Twice_ShouldBeIdempotent_NoDuplicateXp()
    {
        // Scenario (c): повторный override идемпотентен по (target, material) — одна строка,
        // один XP-award, никакого второго MaterialViewedEvent.
        Guid actorAdminId = Guid.NewGuid();
        Guid targetUserId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        AuthenticateAs(actorAdminId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 2);

        await SeedEnrollmentAsync(courseId, targetUserId);

        HttpResponseMessage first = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(targetUserId));
        HttpResponseMessage second = await PostAsJsonAsync(
            $"/progress/materials/{materialId}/view-for-user",
            new MarkMaterialViewedForUserRequest(targetUserId));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        int viewCount = await ExecuteInDb(db =>
            db.MaterialViews.CountAsync(x => x.UserId == targetUserId && x.MaterialId == materialId));
        int moduleItemCount = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x =>
                x.ReferenceId == materialId && x.ItemType == ModuleItemProgressType.MATERIAL));
        int xpCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x =>
                x.UserId == targetUserId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));

        Assert.Equal(1, viewCount);
        Assert.Equal(1, moduleItemCount);
        Assert.Equal(1, xpCount);
    }
}
