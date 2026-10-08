namespace TrainerService.Domain;

/// <summary>
///     A metered AI-usage dimension whose per-user, per-period count is enforced against the configured
///     quota (#614 C2). Each maps to a distinct Redis counter scope + reset period:
///     <list type="bullet">
///         <item><see cref="OPEN_GRADE"/> — inline OPEN_TEXT AI grade (non-MOCK CheckAnswer); daily.</item>
///         <item><see cref="VOICE"/> — voice (transcription) answer; monthly.</item>
///         <item><see cref="MOCK"/> — mock-interview / mock session start; monthly.</item>
///     </list>
/// </summary>
public enum QuotaDimension
{
    OPEN_GRADE,
    VOICE,
    MOCK,
}
