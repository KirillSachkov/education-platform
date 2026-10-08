using TrainerService.Core.Grading;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions;

public static class SessionScoreCalculator
{
    /// <summary>
    /// Computes the synchronous completion score. Every auto-gradable item belongs to the
    /// denominator (an unanswered item is zero); an OPEN_TEXT item participates only after it has
    /// already received an inline score. Deferred MOCK grading recalculates its final score after
    /// the pending open answers are evaluated.
    /// </summary>
    public static int ComputeAtCompletion(IReadOnlyList<TrainingSessionItem> items)
    {
        int total = 0;
        int score = 0;

        foreach (TrainingSessionItem item in items)
        {
            if (AnswerGrader.IsAutoGradable(item.QuestionType))
            {
                total++;
                score += item.ScorePercent ?? 0;
            }
            else if (item.ScorePercent is { } gradedScore)
            {
                total++;
                score += gradedScore;
            }
        }

        return total == 0
            ? 0
            : (int)Math.Round((double)score / total, MidpointRounding.AwayFromZero);
    }
}
