using Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.IntegrationTests.Features.Gamification;

[Collection(nameof(IntegrationTestsFixture))]
public class XpAwardServiceTests : ProgressServiceTestsBase
{
    public XpAwardServiceTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task AwardAsync_WhenSameSourceProcessedTwice_ShouldCreateSingleAward()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid sourceId = Guid.NewGuid();

        Guid enrollmentId = await ExecuteInDb(async dbContext =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            await dbContext.CourseEnrollments.AddAsync(enrollment);
            await dbContext.SaveChangesAsync();
            return enrollment.Id;
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IXpAwardService xpAwardService = scope.ServiceProvider.GetRequiredService<IXpAwardService>();
        ITransactionManager transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        XpAwardCommand command = new(userId, enrollmentId, XpAwardType.ISSUE_APPROVED, sourceId);

        var firstResult = await xpAwardService.AwardAsync(command, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);

        var secondResult = await xpAwardService.AwardAsync(command, CancellationToken.None);
        Assert.True(secondResult.IsSuccess);
        Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);

        UserGamificationStats? stats = await ExecuteInDb(dbContext =>
            dbContext.UserGamificationStats.FirstOrDefaultAsync(x => x.UserId == userId));
        int awardsCount = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x => x.UserId == userId));

        Assert.NotNull(stats);
        Assert.Equal(20, stats!.TotalXp);
        Assert.Equal(1, stats.CurrentLevel);
        Assert.Equal(1, awardsCount);
    }

    [Fact]
    public async Task AwardAsync_WhenLevelGoesUp_PublishesUserLeveledUp()
    {
        // #555 — кривая (appsettings.Tests.json): L1=0, L2=100. Один ISSUE_APPROVED = 20 XP,
        // поэтому набираем 100 XP за 5 разных наград → переход L1→L2 на пятой.
        NoOpOutboxService.Reset();

        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid enrollmentId = await ExecuteInDb(async dbContext =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            await dbContext.CourseEnrollments.AddAsync(enrollment);
            await dbContext.SaveChangesAsync();
            return enrollment.Id;
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IXpAwardService xpAwardService = scope.ServiceProvider.GetRequiredService<IXpAwardService>();
        ITransactionManager transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        for (int i = 0; i < 5; i++)
        {
            XpAwardCommand command = new(userId, enrollmentId, XpAwardType.ISSUE_APPROVED, Guid.NewGuid());
            Assert.True((await xpAwardService.AwardAsync(command, CancellationToken.None)).IsSuccess);
            Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);
        }

        UserLeveledUp evt = Assert.Single(NoOpOutboxService.Published.OfType<UserLeveledUp>());
        Assert.Equal(userId, evt.UserId);
        Assert.Equal(1, evt.PreviousLevel);
        Assert.Equal(2, evt.NewLevel);
        Assert.Equal(100, evt.TotalXp);
    }

    [Fact]
    public async Task AwardAsync_WhenLevelUnchanged_DoesNotPublishUserLeveledUp()
    {
        // Одна награда 20 XP < порога L2 (100) → уровень остаётся 1 → событие не публикуется.
        NoOpOutboxService.Reset();

        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IXpAwardService xpAwardService = scope.ServiceProvider.GetRequiredService<IXpAwardService>();
        ITransactionManager transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        XpAwardCommand command = new(userId, null, XpAwardType.MATERIAL_VIEWED, materialId);
        Assert.True((await xpAwardService.AwardAsync(command, CancellationToken.None)).IsSuccess);
        Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);

        Assert.Empty(NoOpOutboxService.Published.OfType<UserLeveledUp>());
    }

    [Fact]
    public async Task AwardAsync_UserScopedMaterialViewed_WithNullEnrollmentId_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IXpAwardService xpAwardService = scope.ServiceProvider.GetRequiredService<IXpAwardService>();
        ITransactionManager transactionManager = scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        // User-scoped XP: enrollment_id = null, уникальность по (userId, AwardType, SourceId).
        XpAwardCommand command = new(userId, null, XpAwardType.MATERIAL_VIEWED, materialId);

        var firstResult = await xpAwardService.AwardAsync(command, CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);

        var secondResult = await xpAwardService.AwardAsync(command, CancellationToken.None);
        Assert.True(secondResult.IsSuccess);
        Assert.True((await transactionManager.SaveChangesAsync()).IsSuccess);

        UserGamificationStats? stats = await ExecuteInDb(dbContext =>
            dbContext.UserGamificationStats.FirstOrDefaultAsync(x => x.UserId == userId));
        XpAward? award = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.FirstOrDefaultAsync(x =>
                x.UserId == userId
                && x.AwardType == XpAwardType.MATERIAL_VIEWED
                && x.SourceId == materialId));
        int awardsCount = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x => x.UserId == userId));

        Assert.NotNull(stats);
        Assert.Equal(10, stats!.TotalXp);
        Assert.Equal(1, awardsCount);
        Assert.NotNull(award);
        Assert.Null(award.EnrollmentId);
    }
}
