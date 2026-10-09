using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Extensions;
using ProgressService.Domain;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Features.QuizAttempts.Admin;

public sealed record GetQuizAdminStatsQuery(Guid QuizId) : IQuery;

public sealed class GetQuizAdminStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/quizzes/admin/{quizId:guid}/stats",
                async Task<EndpointResult<QuizAdminStatsResponse>> (
                    Guid quizId,
                    [FromServices] GetQuizAdminStatsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetQuizAdminStatsQuery(quizId), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

/// <summary>
///     Drill-in админ-статистика по конкретному тесту (#556, AC5): распределение баллов
///     по 5 бакетам и сложность каждого вопроса (доля верных ответов по всем попыткам).
///     Грузит все <see cref="QuizAttempt"/> квиза (репозиторий материализует JSONB-ответы)
///     + answer-key из ECS и пересчитывает per-вопрос корректность тем же
///     <see cref="QuizAttemptGrader.GradeOne"/>, что и при сабмите — OPEN_TEXT не входит в
///     долю верных (только в число ответивших). Answer-key недоступен (квиз hard-deleted) → 404.
///     Read-only; Tier-1 Users.VIEW.
/// </summary>
public sealed class GetQuizAdminStatsHandler
    : IQueryHandlerWithResult<QuizAdminStatsResponse, GetQuizAdminStatsQuery>
{
    private static readonly (int LowerExclusive, string Bucket)[] _buckets =
    [
        (20, "0-20"),
        (40, "21-40"),
        (60, "41-60"),
        (80, "61-80"),
        (100, "81-100"),
    ];

    private readonly IQuizAttemptRepository _quizAttemptRepository;
    private readonly IEducationContentServiceClient _educationContentServiceClient;

    public GetQuizAdminStatsHandler(
        IQuizAttemptRepository quizAttemptRepository,
        IEducationContentServiceClient educationContentServiceClient)
    {
        _quizAttemptRepository = quizAttemptRepository;
        _educationContentServiceClient = educationContentServiceClient;
    }

    public async Task<Result<QuizAdminStatsResponse, Error>> Handle(
        GetQuizAdminStatsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.QuizId == Guid.Empty)
        {
            return GeneralErrors.ValueIsRequired(nameof(GetQuizAdminStatsQuery.QuizId));
        }

        Guid quizId = query.QuizId;

        Result<QuizAnswerKeyDto, Error> answerKeyResult =
            await _educationContentServiceClient.GetQuizAnswerKeyAsync(quizId, cancellationToken);
        if (answerKeyResult.IsNotFound())
        {
            return answerKeyResult.Error;
        }

        if (answerKeyResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        QuizAnswerKeyDto answerKey = answerKeyResult.Value;

        string title = quizId.ToString();
        Result<IReadOnlyList<QuizSummaryLookupDto>, Error> summariesResult =
            await _educationContentServiceClient.GetQuizSummariesAsync([quizId], cancellationToken);
        if (summariesResult.IsSuccess)
        {
            QuizSummaryLookupDto? summary = summariesResult.Value.FirstOrDefault(s => s.Id == quizId);
            if (summary is not null)
            {
                title = summary.Title;
            }
        }

        IReadOnlyList<QuizAttempt> attempts = await _quizAttemptRepository.GetManyByAsync(
            a => a.QuizId == quizId,
            cancellationToken);

        long attemptsCount = attempts.Count;
        long uniqueUsers = attempts.Select(a => a.UserId).Distinct().Count();
        long passedCount = attempts.Count(a => a.Passed);
        double passRate = attemptsCount == 0
            ? 0
            : Math.Round(100.0 * passedCount / attemptsCount, 1);
        double avgScore = attemptsCount == 0
            ? 0
            : Math.Round(attempts.Average(a => a.ScorePercent), 1);

        // Распределение баллов по 5 бакетам.
        var bucketCounts = new long[_buckets.Length];
        foreach (QuizAttempt attempt in attempts)
        {
            bucketCounts[ResolveBucketIndex(attempt.ScorePercent)]++;
        }

        List<QuizScoreBucketRow> scoreDistribution = _buckets
            .Select((b, i) => new QuizScoreBucketRow(b.Bucket, bucketCounts[i]))
            .ToList();

        // Сложность вопросов: по каждому вопросу answer-key считаем долю верных среди
        // ответивших, пересчитывая корректность из сохранённых ответов тем же грейдером.
        var answersByAttempt = attempts
            .Select(a => a.Answers.ToDictionary(ans => ans.QuestionId))
            .ToList();

        List<QuizQuestionStatsRow> questions = answerKey.Questions
            .Select(question =>
            {
                long answered = 0;
                long correct = 0;

                foreach (Dictionary<Guid, QuizAttemptAnswer> attemptAnswers in answersByAttempt)
                {
                    if (!attemptAnswers.TryGetValue(question.Id, out QuizAttemptAnswer? answer))
                    {
                        continue;
                    }

                    bool hasContent = answer.SelectedOptionIds.Count > 0
                        || !string.IsNullOrWhiteSpace(answer.TextAnswer);
                    if (!hasContent)
                    {
                        continue;
                    }

                    answered++;

                    bool? graded = QuizAttemptGrader.GradeOne(
                        question,
                        answer.SelectedOptionIds,
                        answer.TextAnswer);
                    if (graded == true)
                    {
                        correct++;
                    }
                }

                double correctRate = answered == 0
                    ? 0
                    : Math.Round(100.0 * correct / answered, 1);

                return new QuizQuestionStatsRow(
                    question.Id,
                    question.Text,
                    question.Type,
                    answered,
                    correct,
                    correctRate);
            })
            .ToList();

        return new QuizAdminStatsResponse(
            quizId,
            title,
            attemptsCount,
            uniqueUsers,
            passRate,
            avgScore,
            scoreDistribution,
            questions);
    }

    private static int ResolveBucketIndex(int scorePercent)
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            if (scorePercent <= _buckets[i].LowerExclusive)
            {
                return i;
            }
        }

        return _buckets.Length - 1;
    }
}