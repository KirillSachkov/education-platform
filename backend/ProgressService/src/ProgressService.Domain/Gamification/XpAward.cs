namespace ProgressService.Domain.Gamification;

/// <summary>
/// Ledger начисления XP пользователю. Идемпотентность — по паре (UserId, AwardType, SourceId).
/// <para>
/// EnrollmentId nullable: курсовые награды (<c>ISSUE_APPROVED</c>, <c>MODULE_COMPLETED</c>,
/// <c>PROJECT_COMPLETED</c>) пишутся в конкретный enrollment. <c>MATERIAL_VIEWED</c> — user-scoped
/// и не привязан к enrollment'у: просмотр — факт о пользователе и материале, один раз за всё время.
/// </para>
/// </summary>
public sealed class XpAward
{
    private XpAward(
        Guid userId,
        Guid? enrollmentId,
        XpAwardType awardType,
        Guid sourceId,
        int xpAmount)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        EnrollmentId = enrollmentId;
        AwardType = awardType;
        SourceId = sourceId;
        XpAmount = xpAmount;
        CreatedAt = DateTime.UtcNow;
    }

    private XpAward()
    {
    }

    public Guid Id { get; private set; }

    public uint Version { get; private set; }

    public Guid UserId { get; private set; }

    public Guid? EnrollmentId { get; private set; }

    public XpAwardType AwardType { get; private set; }

    public Guid SourceId { get; private set; }

    public int XpAmount { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Создаёт запись о начисленной награде XP.
    /// </summary>
    public static Result<XpAward, Error> Create(
        Guid userId,
        Guid? enrollmentId,
        XpAwardType awardType,
        Guid sourceId,
        int xpAmount)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (enrollmentId is Guid actualEnrollment && actualEnrollment == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(enrollmentId));
        }

        if (sourceId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(sourceId));
        }

        if (xpAmount <= 0)
        {
            return ProgressErrors.XpAmountMustBePositive(nameof(xpAmount));
        }

        return new XpAward(userId, enrollmentId, awardType, sourceId, xpAmount);
    }
}
