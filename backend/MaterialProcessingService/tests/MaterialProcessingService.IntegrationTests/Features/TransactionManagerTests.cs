using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.Infrastructure.Postgres;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class TransactionManagerTests : MaterialProcessingServiceTestsBase
{
    public TransactionManagerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UniqueViolationCleanup_DetachesRejectedAddedEntries()
    {
        await ExecuteInDb(dbContext =>
        {
            TimecodeGenerationJob rejectedJob = TimecodeGenerationJob.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                ProcessingSourceType.Create("HLS").Value).Value;
            dbContext.TimecodeGenerationJobs.Add(rejectedJob);

            Assert.Equal(EntityState.Added, dbContext.Entry(rejectedJob).State);

            TransactionManager.DetachAddedEntriesAfterUniqueViolation(dbContext);

            Assert.Equal(EntityState.Detached, dbContext.Entry(rejectedJob).State);
            Assert.DoesNotContain(
                dbContext.ChangeTracker.Entries(),
                entry => entry.State == EntityState.Added);
            return Task.CompletedTask;
        });
    }
}
