using EducationContentService.Contracts.Quizzes;
using ProgressService.Core.Features.QuizAttempts;
using ProgressService.Domain.Quizzes;

namespace ProgressService.IntegrationTests.Features.QuizAttempts;

public class QuizAttemptGraderTests
{
    [Theory]
    [InlineData("True False", "true,false", true)]
    [InlineData("1, 4, 9", "149", true)]
    [InlineData("Ёж 42", "ёж-42", true)]
    [InlineData("class", "struct", false)]
    [InlineData("149", "194", false)]
    public void ExactText_GradesNormalizedAnswer_AndPreservesReveal(
        string reference, string answer, bool expectedCorrect)
    {
        Guid questionId = Guid.NewGuid();
        var key = new QuizAnswerKeyDto(
            Guid.NewGuid(), "MATERIAL_CHECK", 70,
            [new QuizAnswerKeyQuestionDto(
                questionId, "EXACT_TEXT", "Вопрос", null, null, [], reference,
                Explanation: "Пояснение")]);
        QuizAttemptAnswer submitted = QuizAttemptAnswer.Create(questionId, [], answer).Value;

        QuizAttemptGrading result = QuizAttemptGrader.Grade(key, [submitted]);
        var question = Assert.Single(result.Questions);

        Assert.Equal(expectedCorrect, question.Correct);
        Assert.Equal(expectedCorrect ? 100 : 0, result.ScorePercent);
        Assert.Equal(expectedCorrect, result.Passed);
        Assert.Equal(reference, question.ReferenceAnswer);
        Assert.Equal("Пояснение", question.Explanation);
        Assert.Equal(expectedCorrect, QuizAttemptGrader.GradeOne(key.Questions[0], [], answer));
    }
}