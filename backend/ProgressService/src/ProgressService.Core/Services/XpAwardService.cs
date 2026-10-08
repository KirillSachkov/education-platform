using Microsoft.Extensions.Options;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Configuration;
using ProgressService.Core.Database;
using ProgressService.Core.Extensions;
using ProgressService.Domain.Gamification;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.Core.Services;

/// <summary>
/// Реализует начисление XP пользователю по доменным событиям прогресса.
/// </summary>
public sealed class XpAwardService : IXpAwardService
{
    private readonly ICourseEnrollmentRepository _courseEnrollmentRepository;
    private readonly IUserStatsRepository _userStatsRepository;
    private readonly IXpAwardRepository _xpAwardRepository;
    private readonly IXpLevelPolicy _xpLevelPolicy;
    private readonly IOutboxService _outboxService;
    private readonly GamificationOptions _options;

    public XpAwardService(
        ICourseEnrollmentRepository courseEnrollmentRepository,
        IUserStatsRepository userStatsRepository,
        IXpAwardRepository xpAwardRepository,
        IXpLevelPolicy xpLevelPolicy,
        IOutboxService outboxService,
        IOptions<GamificationOptions> options)
    {
        _courseEnrollmentRepository = courseEnrollmentRepository;
        _userStatsRepository = userStatsRepository;
        _xpAwardRepository = xpAwardRepository;
        _xpLevelPolicy = xpLevelPolicy;
        _outboxService = outboxService;
        _options = options.Value;
    }

    /// <summary>
    /// Начисляет XP по запросу, создаёт ledger-запись награды и обновляет агрегированную статистику пользователя.
    /// </summary>
    public async Task<UnitResult<Error>> AwardAsync(XpAwardCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty && command.EnrollmentId is null)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.UserId));
        }

        if (command.EnrollmentId is Guid actualEnrollment && actualEnrollment == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.EnrollmentId));
        }

        if (command.SourceId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.SourceId));
        }

        // Резолвим userId: для курсовых наград (EnrollmentId != null) — через enrollment
        // (страховка от stale event'ов + нужен userId). Для user-scoped берём из команды.
        Guid userId;
        if (command.EnrollmentId is Guid enrollmentIdToVerify)
        {
            Result<Domain.Enrollments.CourseEnrollment, Error> enrollmentResult = await _courseEnrollmentRepository
                .GetByAsync(x => x.Id == enrollmentIdToVerify, cancellationToken);
            if (enrollmentResult.IsFailure)
            {
                return enrollmentResult.Error;
            }

            userId = enrollmentResult.Value.UserId;
        }
        else
        {
            userId = command.UserId;
        }

        XpAwardType awardType = command.AwardType;
        Guid sourceId = command.SourceId;
        Guid awardUserId = userId;
        bool awardExists = await _xpAwardRepository
            .ExistsAsync(
                x => x.UserId == awardUserId && x.AwardType == awardType && x.SourceId == sourceId,
                cancellationToken);

        if (awardExists)
        {
            return UnitResult.Success<Error>();
        }

        Result<int, Error> xpAmountResult = ResolveXpAmount(command.AwardType);
        if (xpAmountResult.IsFailure)
        {
            return xpAmountResult.Error;
        }

        int xpAmount = xpAmountResult.Value;

        Result<XpAward, Error> xpAwardResult = XpAward.Create(
            userId,
            command.EnrollmentId,
            command.AwardType,
            command.SourceId,
            xpAmount);
        if (xpAwardResult.IsFailure)
        {
            return xpAwardResult.Error;
        }

        Result<UserGamificationStats, Error> statsResult = await _userStatsRepository
            .GetByAsync(x => x.UserId == userId, cancellationToken);
        if (statsResult.IsFailureExceptNotFound())
        {
            return statsResult.Error;
        }

        UserGamificationStats stats;
        if (statsResult.IsFailure)
        {
            Result<UserGamificationStats, Error> createStatsResult = UserGamificationStats.Create(
                userId,
                _xpLevelPolicy.ResolveLevel(0));
            if (createStatsResult.IsFailure)
            {
                return createStatsResult.Error;
            }

            stats = createStatsResult.Value;
            await _userStatsRepository.AddAsync(stats, cancellationToken);
        }
        else
        {
            stats = statsResult.Value;
        }

        int previousLevel = stats.CurrentLevel;
        int nextTotalXp = stats.TotalXp + xpAmount;
        int nextLevel = _xpLevelPolicy.ResolveLevel(nextTotalXp);

        UnitResult<Error> addXpResult = stats.AddXp(xpAmount, nextLevel);
        if (addXpResult.IsFailure)
        {
            return addXpResult.Error;
        }

        await _xpAwardRepository.AddAsync(xpAwardResult.Value, cancellationToken);

        // Level-up празднование (#555): публикуем integration event только на ПОДЪЁМЕ уровня.
        // Гейт `nextLevel > previousLevel` сам по себе подразумевает nextLevel >= 2 (stats всегда
        // стартуют с уровня 1 = ResolveLevel(0)), поэтому baseline 1 не «празднуется». Дубли
        // защищены ledger-идемпотентностью выше (awardExists short-circuit). Outbox буферизуется
        // в DbContext и флэшится TransactionManager'ом вызывающего handler'а.
        if (nextLevel > previousLevel)
        {
            await _outboxService.PublishAsync(
                new UserLeveledUp(userId, previousLevel, nextLevel, nextTotalXp));
        }

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Возвращает размер награды XP для указанного типа действия из конфигурации.
    /// </summary>
    private Result<int, Error> ResolveXpAmount(XpAwardType awardType) =>
        awardType switch
        {
            XpAwardType.ISSUE_APPROVED => _options.Awards.IssueApproved,
            XpAwardType.MODULE_COMPLETED => _options.Awards.ModuleCompleted,
            XpAwardType.PROJECT_COMPLETED => _options.Awards.ProjectCompleted,
            XpAwardType.MATERIAL_VIEWED => _options.Awards.MaterialViewed,
            _ => GeneralErrors.ValueIsInvalid(nameof(awardType))
        };

    /// <summary>
    /// Отзывает ранее выданную награду XP по бизнес-источнику. Идемпотентен: если награды нет,
    /// возвращает успех. Если есть — удаляет ledger-запись, списывает XP и пересчитывает уровень
    /// (он может понизиться).
    /// </summary>
    public async Task<UnitResult<Error>> RevokeAsync(XpAwardCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty && command.EnrollmentId is null)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.UserId));
        }

        if (command.EnrollmentId is Guid actualEnrollment && actualEnrollment == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.EnrollmentId));
        }

        if (command.SourceId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(command.SourceId));
        }

        Guid userId;
        if (command.EnrollmentId is Guid enrollmentIdToVerify)
        {
            Result<Domain.Enrollments.CourseEnrollment, Error> enrollmentResult = await _courseEnrollmentRepository
                .GetByAsync(x => x.Id == enrollmentIdToVerify, cancellationToken);
            if (enrollmentResult.IsFailure)
            {
                return enrollmentResult.Error;
            }

            userId = enrollmentResult.Value.UserId;
        }
        else
        {
            userId = command.UserId;
        }

        XpAwardType awardType = command.AwardType;
        Guid sourceId = command.SourceId;
        Guid awardUserId = userId;

        XpAward? award = await _xpAwardRepository.FindByAsync(
            x => x.UserId == awardUserId && x.AwardType == awardType && x.SourceId == sourceId,
            cancellationToken);

        if (award is null)
        {
            // Идемпотентность: награды нет — нечего отзывать.
            return UnitResult.Success<Error>();
        }

        Result<UserGamificationStats, Error> statsResult = await _userStatsRepository
            .GetByAsync(x => x.UserId == userId, cancellationToken);
        if (statsResult.IsFailure)
        {
            return statsResult.Error;
        }

        UserGamificationStats stats = statsResult.Value;

        int amountToRevoke = Math.Min(award.XpAmount, stats.TotalXp);
        if (amountToRevoke <= 0)
        {
            // Награда есть, но статистика уже на 0 (или ниже — теоретически) — просто удаляем
            // ledger без изменения stats, иначе RevokeXp вернёт ошибку.
            _xpAwardRepository.Remove(award);
            return UnitResult.Success<Error>();
        }

        int nextTotalXp = stats.TotalXp - amountToRevoke;
        int nextLevel = _xpLevelPolicy.ResolveLevel(nextTotalXp);

        UnitResult<Error> revokeResult = stats.RevokeXp(amountToRevoke, nextLevel);
        if (revokeResult.IsFailure)
        {
            return revokeResult.Error;
        }

        _xpAwardRepository.Remove(award);

        return UnitResult.Success<Error>();
    }
}
