using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Certificates;

namespace ProgressService.Core.Features.Certificates.UseCases;

public sealed record ClaimCourseCertificateCommand(Guid CourseId) : ICommand;

public sealed class ClaimCourseCertificateCommandValidator : AbstractValidator<ClaimCourseCertificateCommand>
{
    public ClaimCourseCertificateCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(ClaimCourseCertificateCommand.CourseId)));
    }
}

public sealed class ClaimCourseCertificateEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/certificate/claim",
                async Task<EndpointResult<CourseCertificateResponse>> (
                    Guid courseId,
                    ClaimCourseCertificateHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new ClaimCourseCertificateCommand(courseId),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Клейм сертификата о прохождении курса (issue #467). Идемпотентен: существующий
///     сертификат пары (user, course) возвращается как есть — до любых походов в ECS
///     (поэтому выданный сертификат переживает удаление курса). Completion-чек зеркалит
///     счётчики course-progress query (<c>GetCourseLearningState</c>/<c>GetMyCourseProgress</c>):
///     материалы — <c>material_views ∩ blueprint.MaterialIds</c> c <c>is_completed=TRUE</c>
///     против <c>TotalMaterials</c>; для Kind=COURSE дополнительно все уникальные задания
///     блюпринта должны быть COMPLETED в <c>issue_progress</c> (INTENSIVE/MARATHON — только
///     материалы); квизы (ST-13 #493) — distinct passed
///     <c>quiz_attempts.quiz_id ∩ blueprint.QuizIds</c> против <c>TotalQuizzes</c> для ВСЕХ
///     Kind (квиз легитимен и в интенсиве/марафоне). Снапшоты: title из блюпринта, имя —
///     display name из Auth с fallback'ом на username. Без outbox-событий. Tier-3: первый
///     клейм требует текущего entitlement на курс (ResourceTypes.COURSE) — отозванный grant
///     не может получить НОВЫЙ сертификат.
/// </summary>
public sealed class ClaimCourseCertificateHandler
    : ICommandHandler<CourseCertificateResponse, ClaimCourseCertificateCommand>
{
    // #650: сертификат выдаётся при прохождении ≥80% всех элементов программы (материалы +
    // задания + тесты) суммарно — владелец снизил порог со 100%. Доля считается так же, как
    // progressPercent в GetCourseLearningState, поэтому фронтовая кнопка совпадает с backend-gate.
    private const decimal COMPLETION_THRESHOLD = 0.80m;

    private readonly IValidator<ClaimCourseCertificateCommand> _validator;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly ICourseCertificateRepository _certificateRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ILogger<ClaimCourseCertificateHandler> _logger;

    public ClaimCourseCertificateHandler(
        IValidator<ClaimCourseCertificateCommand> validator,
        IEducationContentServiceClient educationContentServiceClient,
        IAuthServiceClient authServiceClient,
        ICourseCertificateRepository certificateRepository,
        IEntitlementChecker entitlementChecker,
        ITransactionManager transactionManager,
        UserScopedData user,
        ILogger<ClaimCourseCertificateHandler> logger)
    {
        _validator = validator;
        _educationContentServiceClient = educationContentServiceClient;
        _authServiceClient = authServiceClient;
        _certificateRepository = certificateRepository;
        _entitlementChecker = entitlementChecker;
        _transactionManager = transactionManager;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CourseCertificateResponse, Error>> Handle(
        ClaimCourseCertificateCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Guid userId = _user.UserId;
        Guid courseId = command.CourseId;

        // Идемпотентность — до похода в ECS: выданный сертификат возвращается даже
        // если курс уже удалён/недоступен.
        CourseCertificate? existing = await _certificateRepository.GetByAsync(
            c => c.UserId == userId && c.CourseId == courseId,
            cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing);
        }

        // Tier-3: клейм — student-write, требует ТЕКУЩЕГО доступа к курсу (revoked
        // grant = refund/abuse → новый сертификат не выдаём). Уже выданный сертификат
        // возвращается выше без проверки — документ остаётся у владельца навсегда.
        AccessDecision accessDecision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            ResourceTypes.COURSE,
            courseId,
            cancellationToken);
        if (!accessDecision.IsGranted)
        {
            return ProgressErrors.CertificateCourseAccessDenied();
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([courseId]),
                cancellationToken);
        if (blueprintResult.IsFailure)
        {
            return blueprintResult.Error;
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        if (blueprint is null)
        {
            return Error.NotFound(
                "course.progress.blueprint.not.found",
                $"Course progress blueprint not found for course {courseId}");
        }

        // #650: «пусто» = нет НИ одного элемента программы (материалы + задания + тесты).
        // Раньше гейт был TotalMaterials==0, но курс из одних тестов/заданий сертифицируем,
        // и фронтовый isCertificateEligible гейтит по totalItems>0 — держим симметрию.
        if (blueprint.TotalItems == 0)
        {
            return ProgressErrors.CertificateCourseEmpty();
        }

        UnitResult<Error> completionResult = await CheckCompletionAsync(blueprint, cancellationToken);
        if (completionResult.IsFailure)
        {
            return completionResult.Error;
        }

        Result<string, Error> holderNameResult = await ResolveHolderNameAsync(cancellationToken);
        if (holderNameResult.IsFailure)
        {
            return holderNameResult.Error;
        }

        Result<CourseCertificate, Error> certificateResult = CourseCertificate.Create(
            userId,
            courseId,
            blueprint.Title,
            holderNameResult.Value);
        if (certificateResult.IsFailure)
        {
            return certificateResult.Error;
        }

        CourseCertificate certificate = certificateResult.Value;

        // Гонка двух конкурентных claim'ов: unique-индекс (user_id, course_id) даст 23505,
        // TransactionManager.HandlePostgresException вернёт typed-ошибку — дублей не будет.
        await _certificateRepository.AddAsync(certificate, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Course certificate issued. UserId: {UserId}, CourseId: {CourseId}, SerialNumber: {SerialNumber}",
            userId,
            courseId,
            certificate.SerialNumber);

        return ToResponse(certificate);
    }

    private async Task<UnitResult<Error>> CheckCompletionAsync(
        CourseProgressBlueprintDto blueprint,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();
        Guid[] materialIds = blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray();

        // is_completed=TRUE — только явно «Изучено» (issue #285), как во всех
        // course-progress счётчиках. Silent track-view'ы прогрессом не считаются.
        const string materialsSql = """
                                    SELECT COUNT(*)::integer
                                    FROM material_views mv
                                    WHERE mv.user_id = @UserId
                                      AND mv.material_id = ANY(@MaterialIds)
                                      AND mv.is_completed = TRUE;
                                    """;

        int completedMaterials = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                materialsSql,
                new { UserId = _user.UserId, MaterialIds = materialIds },
                cancellationToken: cancellationToken));

        bool isRegularCourse = string.Equals(blueprint.Kind, "COURSE", StringComparison.Ordinal);

        int completedIssues = 0;
        if (isRegularCourse && blueprint.TotalUniqueIssues > 0)
        {
            // Задания — enrollment-scoped (зеркало GetMyCourseProgress): distinct
            // COMPLETED issue_progress по прогресс-якорю пользователя на этом курсе.
            const string issuesSql = """
                                     SELECT COUNT(DISTINCT ip.issue_id)::integer
                                     FROM issue_progress ip
                                     JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                                     WHERE ce.user_id = @UserId
                                       AND ce.course_id = @CourseId
                                       AND ip.status = 'COMPLETED';
                                     """;

            completedIssues = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    issuesSql,
                    new { UserId = _user.UserId, CourseId = blueprint.CourseId },
                    cancellationToken: cancellationToken));
        }

        int passedQuizzes = 0;
        if (blueprint.TotalQuizzes > 0)
        {
            // Квизы — user-scoped (как материалы), для ВСЕХ Kind: distinct passed
            // quiz_attempts ∩ blueprint.QuizIds (ST-13 #493).
            Guid[] quizIds = blueprint.QuizIds as Guid[] ?? blueprint.QuizIds.ToArray();

            const string quizzesSql = """
                                      SELECT COUNT(DISTINCT qa.quiz_id)::integer
                                      FROM quiz_attempts qa
                                      WHERE qa.user_id = @UserId
                                        AND qa.quiz_id = ANY(@QuizIds)
                                        AND qa.passed = TRUE;
                                      """;

            passedQuizzes = await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    quizzesSql,
                    new { UserId = _user.UserId, QuizIds = quizIds },
                    cancellationToken: cancellationToken));
        }

        // Доля прохождения = (изученные материалы + принятые задания + сданные тесты) от
        // blueprint.TotalItems. Числитель/знаменатель зеркалят completedItems/TotalItems из
        // GetCourseLearningState (тот же progressPercent, что видит студент). INTENSIVE/MARATHON:
        // заданий в TotalItems нет (их нельзя создать) → порог по материалам+тестам. Пустой курс
        // (0 материалов → 0 элементов) уже отсечён в Handle через CertificateCourseEmpty.
        int completedItems = completedMaterials + completedIssues + passedQuizzes;
        int totalItems = blueprint.TotalItems;

        if (totalItems > 0 && (decimal)completedItems / totalItems >= COMPLETION_THRESHOLD)
        {
            return UnitResult.Success<Error>();
        }

        int currentPercent = totalItems == 0
            ? 0
            : (int)Math.Floor((decimal)completedItems / totalItems * 100);
        int requiredPercent = (int)(COMPLETION_THRESHOLD * 100);

        return ProgressErrors.CertificateCourseNotCompleted(
            $"пройдено {completedItems} из {totalItems} элементов программы " +
            $"({currentPercent}%), нужно ≥ {requiredPercent}%");
    }

    private async Task<Result<string, Error>> ResolveHolderNameAsync(CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> usersResult =
            await _authServiceClient.GetUsersByIdsAsync([_user.UserId], cancellationToken);
        if (usersResult.IsFailure)
        {
            return ProgressErrors.AuthServiceUnavailable();
        }

        AuthUserLookupDto? holder = usersResult.Value.FirstOrDefault(u => u.UserId == _user.UserId);
        string? holderName = !string.IsNullOrWhiteSpace(holder?.Name)
            ? holder.Name
            : holder?.Username;

        if (string.IsNullOrWhiteSpace(holderName))
        {
            return ProgressErrors.CertificateHolderNameUnavailable();
        }

        return holderName;
    }

    private static CourseCertificateResponse ToResponse(CourseCertificate certificate) =>
        new(
            certificate.Id,
            certificate.SerialNumber,
            certificate.HolderName,
            certificate.CourseTitle,
            certificate.CourseId,
            certificate.IssuedAt);
}
