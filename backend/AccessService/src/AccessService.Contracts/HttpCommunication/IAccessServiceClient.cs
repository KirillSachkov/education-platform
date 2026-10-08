using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Dtos;

namespace AccessService.Contracts.HttpCommunication;

public interface IAccessServiceClient
{
    /// <summary>
    /// S2S: Telegram-инфо плана — настроенное приветствие (<see cref="PlanTelegramInfoDto.WelcomeMessage"/>)
    /// + имя оффера. Используется TelegramBotService для поста приветствия в группу при входе
    /// участника и для именования оффера в claim-сообщении.
    /// </summary>
    Task<Result<PlanTelegramInfoDto, Error>> GetPlanTelegramInfoAsync(
        Guid planId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues the author's default FULL_ALL / LEARN_ALL plan-grant to a user. Idempotent —
    /// re-calling for the same (user, author, ACTIVE grant) returns the existing one.
    /// Returns 404 if the author has no default plan; caller should handle silently.
    /// </summary>
    Task<Result<PlanGrantDto, Error>> GrantLifetimeForAuthorAsync(
        Guid userId,
        Guid authorId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Issues a grant on a specific plan. Idempotent on (user, plan, ACTIVE).
    /// Whitelisted sources: GITHUB_ORG / TELEGRAM_F1 / ADMIN_GRANT.
    /// </summary>
    Task<Result<PlanGrantDto, Error>> GrantByPlanAsync(
        Guid userId,
        Guid planId,
        string source,
        Guid? sourceRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the user's currently ACTIVE grants. Used by TelegramBotService F2 handler
    /// to decide whether to approve a chat-join-request based on existing plan-grants.
    /// </summary>
    Task<Result<IReadOnlyList<PlanGrantDto>, Error>> GetUserGrantsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns distinct user IDs holding an active FULL_ALL / LEARN_ALL plan-grant.
    /// The author id parameter is route-compatible only: full access is global now.
    /// Used by NotificationService at <c>course.created</c> to fan-out auto-subscribe
    /// so full-access grantees receive notifications about new courses.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>, Error>> GetLifetimeGranteeUserIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Keyset-страница пользователей, чей активный grant покрывает <paramref name="courseId"/>
    /// (global FULL/LEARN ∪ COURSE-of-course; FREE исключён). Источник roster'а
    /// "кто на курсе X" в derive-модели (epic access-derive-model, Phase 0). Caller
    /// передаёт <paramref name="authorId"/>, чтобы избежать ECS-хопа; опциональный
    /// <paramref name="userIdsFilter"/> — name-search, резолвится caller'ом через
    /// AuthService до вызова.
    /// </summary>
    Task<Result<CourseGranteesPage, Error>> GetCourseGranteesAsync(
        Guid courseId,
        Guid authorId,
        IReadOnlyList<Guid>? userIdsFilter,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Набор courseId'ов, покрытых активными grant'ами пользователя (explicit COURSE ∪
    /// вся платформа для FULL_ALL/LEARN_ALL ∪ legacy FREE-author). Источник "мои курсы" в derive-модели (epic
    /// access-derive-model, Phase 0). Опциональный <paramref name="authorId"/> фильтрует
    /// до курсов конкретного автора (зеркало <c>GetMyCourseProgress?authorId=</c>).
    /// </summary>
    Task<Result<CoveredCoursesResult, Error>> GetUserCoveredCoursesAsync(
        Guid userId,
        Guid? authorId,
        CancellationToken cancellationToken);
}
