using Microsoft.EntityFrameworkCore;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

public sealed class PerformanceIndexTests : AssignmentReviewServiceTestsBase
{
    public PerformanceIndexTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Hot_background_and_rate_limit_queries_have_supporting_indexes()
    {
        await ExecuteInDbAsync(async db =>
        {
            List<string> indexes = await db.Database.SqlQueryRaw<string>(
                    """
                    SELECT indexname AS "Value"
                    FROM pg_indexes
                    WHERE schemaname = 'assignment_review'
                    """)
                .ToListAsync();

            Assert.Contains("ix_ai_reviews_active_liveness", indexes);
            Assert.Contains("ix_ai_reviews_queued_updated_at", indexes);
            Assert.Contains("ix_ai_reviews_issue_id", indexes);
            Assert.Contains("ix_ai_reviews_author_id", indexes);
            Assert.Contains("ix_ai_review_iterations_started_review", indexes);
            Assert.Contains("ix_student_pr_messages_review_created", indexes);
        });
    }
}
