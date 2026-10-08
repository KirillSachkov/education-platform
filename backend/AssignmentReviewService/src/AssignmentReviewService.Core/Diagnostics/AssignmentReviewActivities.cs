using System.Diagnostics;

namespace AssignmentReviewService.Core.Diagnostics;

/// <summary>
///     Phase 12 (#15): trace ActivitySource для ARS pipeline. Имя совпадает с
///     meter'ом (<c>EducationPlatform.AssignmentReview</c>) — Tempo связывает
///     spans + metrics через service.name + meter.name labels.
/// </summary>
public static class AssignmentReviewActivities
{
    public const string SOURCE_NAME = "EducationPlatform.AssignmentReview";

    public static readonly ActivitySource Source = new(SOURCE_NAME);

    public static Activity? StartIteration(Guid reviewId) =>
        Source.StartActivity("assignment-review.iteration.run", ActivityKind.Internal)
            ?.AddTag("review.id", reviewId);

    public static Activity? StartLlmCall(string model) =>
        Source.StartActivity("assignment-review.llm.call", ActivityKind.Client)
            ?.AddTag("ai.model", model);

    public static Activity? StartGitHubPost(string repoFullName, int pullNumber) =>
        Source.StartActivity("assignment-review.github.post_review", ActivityKind.Client)
            ?.AddTag("vcs.repo", repoFullName)
            ?.AddTag("vcs.pull_number", pullNumber);
}
