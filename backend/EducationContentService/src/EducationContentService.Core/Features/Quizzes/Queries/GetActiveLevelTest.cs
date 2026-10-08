using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Core.Diagnostics;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetActiveLevelTestQuery : IQuery;

/// <summary>
///     Секция теста уровня в студенческой проекции — только ключ и заголовок
///     (для прогресса по секциям в UI). Weight / RecommendedCourseId / пороги —
///     скоринговые внутренности, наружу не отдаются.
/// </summary>
public sealed record LevelTestSectionStudentDto(string Key, string Title);

/// <summary>
///     Студенческая проекция активного теста уровня. Вопросы — в answer-stripped
///     <see cref="QuizQuestionStudentDto"/> (CorrectOptionIds / ReferenceAnswer
///     отсутствуют в контракте физически). <see cref="Sections"/> пуст, если
///     у квиза нет LevelTestConfig.
/// </summary>
public sealed record LevelTestStudentDto(
    Guid Id,
    string Title,
    IReadOnlyList<QuizQuestionStudentDto> Questions,
    IReadOnlyList<LevelTestSectionStudentDto> Sections,
    int TotalQuestions);

public sealed class GetActiveLevelTestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("quizzes/level-test/active", async Task<EndpointResult<LevelTestStudentDto>> (
                    [FromServices] GetActiveLevelTestHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetActiveLevelTestQuery(), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

/// <summary>
///     Публичная точка входа level-test funnel'а: отдаёт АКТИВНЫЙ тест уровня —
///     самый свежий опубликованный квиз с <see cref="QuizPurpose.LEVEL_TEST"/>.
///     «Самый свежий» = max(updated_at) (Publish() бампает UpdatedAt; отдельной
///     published_at колонки нет), tie-break по id (v7 — time-ordered).
///     Tier-1 anonymous read: тест уровня — промо-PUBLIC контент, entitlement
///     не проверяется; студенческая проекция не содержит ответов и скоринговых
///     внутренностей конфига (пороги/веса/рекомендации остаются server-side).
/// </summary>
public sealed class GetActiveLevelTestHandler : IQueryHandlerWithResult<LevelTestStudentDto, GetActiveLevelTestQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly EducationContentMetrics _metrics;

    public GetActiveLevelTestHandler(
        ITransactionManager transactionManager,
        IQuizzesRepository quizzesRepository,
        EducationContentMetrics metrics)
    {
        _transactionManager = transactionManager;
        _quizzesRepository = quizzesRepository;
        _metrics = metrics;
    }

    public async Task<Result<LevelTestStudentDto, Error>> Handle(
        GetActiveLevelTestQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT q.id
                           FROM quizzes q
                           WHERE q.purpose = 'LEVEL_TEST'
                             AND q.status = 'PUBLISHED'
                           ORDER BY q.updated_at DESC, q.id DESC
                           LIMIT 1;
                           """;

        Guid? activeQuizId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        if (activeQuizId is null)
            return EducationErrors.LevelTestNotFound();

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == activeQuizId.Value, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.LevelTestNotFound();

        // Funnel-метрика (#482): успешная выдача теста — proxy «начали тест».
        _metrics.IncrementLevelTestFetched();

        return ToStudentDto(quizResult.Value);
    }

    private static LevelTestStudentDto ToStudentDto(Quiz quiz) =>
        new(
            quiz.Id,
            quiz.Title.Value,
            quiz.Questions
                .Select(q => new QuizQuestionStudentDto(
                    q.Id,
                    q.Type.ToString(),
                    q.Text,
                    q.Section,
                    q.Difficulty?.ToString(),
                    q.Options.Select(o => new QuizOptionDto(o.Id, o.Text)).ToList()))
                .ToList(),
            quiz.LevelTestConfig?.Sections
                .Select(s => new LevelTestSectionStudentDto(s.Key, s.Title))
                .ToList() ?? [],
            quiz.Questions.Count);
}
