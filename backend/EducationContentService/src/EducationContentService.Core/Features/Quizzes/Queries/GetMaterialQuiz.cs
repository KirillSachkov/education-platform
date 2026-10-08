using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Quizzes.Queries;

public sealed record GetMaterialQuizQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialQuizEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("materials/{materialId:guid}/quiz", async Task<EndpointResult<QuizStudentDto>> (
                    [FromRoute] Guid materialId,
                    [FromServices] GetMaterialQuizHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialQuizQuery(materialId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

/// <summary>
///     Студенческая проекция опубликованного квиза материала — БЕЗ CorrectOptionIds и
///     ReferenceAnswer (их нет в <see cref="QuizStudentDto"/> физически).
///     После инверсии Quiz↔Material (#489) квиз резолвится через <c>materials.quiz_id</c>.
///     Доступ гейтится в ДВА слоя — действует строжайший из двух (#490):
///     (1) по МАТЕРИАЛУ ровно как в <c>GetMaterialDetail</c>, (2) по собственному
///     <c>Quiz.AccessType</c> — ENROLLED-квиз на PUBLIC-материале остаётся закрытым.
///     Оба слоя: author / PUBLIC — short-circuit по данным БД, остальное — через
///     <see cref="IEntitlementChecker"/> (admin bypass внутри checker'а);
///     аноним без доступа → 401, авторизованный → 403.
///     Квиз отдаётся только PUBLISHED — иначе 404.
/// </summary>
public sealed class GetMaterialQuizHandler : IQueryHandlerWithResult<QuizStudentDto, GetMaterialQuizQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;
    private readonly ILogger<GetMaterialQuizHandler> _logger;

    public GetMaterialQuizHandler(
        ITransactionManager transactionManager,
        IQuizzesRepository quizzesRepository,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData,
        ILogger<GetMaterialQuizHandler> logger)
    {
        _transactionManager = transactionManager;
        _quizzesRepository = quizzesRepository;
        _entitlementChecker = entitlementChecker;
        _userData = userData;
        _logger = logger;
    }

    public async Task<Result<QuizStudentDto, Error>> Handle(
        GetMaterialQuizQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               m.id,
                               m.author_id,
                               m.access_type,
                               m.quiz_id
                           FROM materials m
                           WHERE m.id = @MaterialId
                             AND (m.status = 'PUBLISHED' OR m.author_id = @UserId);
                           """;

        var parameters = new
        {
            query.MaterialId,
            UserId = _userData.IsAuthenticated ? _userData.UserId : Guid.Empty
        };

        MaterialAccessRow? row = await connection.QueryFirstOrDefaultAsync<MaterialAccessRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (row is null)
            return EducationErrors.MaterialNotFound(query.MaterialId);

        // Short-circuit по данным из БД — зеркалит GetMaterialDetail: PUBLIC — всегда
        // доступен, автору — всегда доступен (экономит Redis round-trips, PUBLIC
        // устойчив к падению Redis).
        bool isAuthor = _userData.IsAuthenticated && row.AuthorId == _userData.UserId;
        bool isPublic = string.Equals(row.AccessType, "PUBLIC", StringComparison.Ordinal);

        if (!isAuthor && !isPublic)
        {
            AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
                _userData.ToAccessSubject(),
                ResourceTypes.MATERIAL,
                query.MaterialId,
                cancellationToken);

            if (!decision.IsGranted)
            {
                _logger.LogInformation(
                    "Access denied to quiz of material {MaterialId} for user {UserId}",
                    query.MaterialId, _userData.UserId);

                return _userData.IsAuthenticated
                    ? EducationErrors.AccessDenied()
                    : EducationErrors.UnauthorizedAccess();
            }
        }

        if (row.QuizId is null)
            return EducationErrors.MaterialQuizNotFound(query.MaterialId);

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == row.QuizId && q.Status == PublicationStatus.PUBLISHED,
            cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.MaterialQuizNotFound(query.MaterialId);

        Quiz quiz = quizResult.Value;

        // Второй слой (#490): у квиза собственный AccessType — действует строжайший
        // из двух гейтов. ENROLLED-квиз на PUBLIC-материале остаётся закрытым.
        // Тот же контракт, что у GET /quizzes/{id}/student: автор квиза / PUBLIC —
        // short-circuit, остальное — quiz-entitlement (admin bypass внутри checker'а).
        bool isQuizAuthor = _userData.IsAuthenticated && quiz.AuthorId == _userData.UserId;
        bool isQuizPublic = quiz.AccessType == AccessType.PUBLIC;

        if (!isQuizAuthor && !isQuizPublic)
        {
            AccessDecision quizDecision = await _entitlementChecker.CheckAccessAsync(
                _userData.ToAccessSubject(),
                ResourceTypes.QUIZ,
                quiz.Id,
                cancellationToken);

            if (!quizDecision.IsGranted)
            {
                _logger.LogInformation(
                    "Access denied to quiz {QuizId} of material {MaterialId} for user {UserId}",
                    quiz.Id, query.MaterialId, _userData.UserId);

                return _userData.IsAuthenticated
                    ? EducationErrors.AccessDenied()
                    : EducationErrors.UnauthorizedAccess();
            }
        }

        return QuizDtoMapper.ToStudentDto(quiz, query.MaterialId);
    }

    private sealed class MaterialAccessRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string AccessType { get; init; } = null!;
        public Guid? QuizId { get; init; }
    }
}
