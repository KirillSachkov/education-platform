using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;

namespace TrainerService.Core.Features.Shared;

/// <summary>
///     Single server-side redaction point for PRO-locked questions (#674). When a question is locked
///     for the caller (<c>!IsFreeSample &amp;&amp; !hasPro &amp;&amp; !isAdmin</c>), the content-bearing fields are
///     nulled so no stem / options / answer-key / reference / explanation / feedback ever reaches a
///     non-PRO client — only safe metadata survives (ids, type, difficulty, section, IsLocked,
///     LockReason). Applied in EVERY question-returning mapper; the lock decision itself is made by
///     the caller, this just enforces the blanking uniformly.
/// </summary>
public static class LockedContentRedactor
{
    public const string LOCK_REASON_PRO_REQUIRED = TrainerProAccessPolicy.LOCK_REASON_PRO_REQUIRED;

    /// <summary>Blanks the session-item content (text/options/key/reference/explanation/feedback) when locked.</summary>
    public static SessionItemDto RedactLocked(SessionItemDto dto) =>
        dto.IsLocked
            ? dto with
            {
                QuestionText = null,
                Options = [],
                CorrectOptionIds = null,
                ReferenceAnswer = null,
                Explanation = null,
                Feedback = null,
            }
            : dto;

    /// <summary>Blanks the question-list item stem when locked (metadata still lists that it exists).</summary>
    public static QuestionListItemDto RedactLocked(QuestionListItemDto dto) =>
        dto.IsLocked ? dto with { Stem = null } : dto;

    /// <summary>Blanks the SRS-due item stem when locked.</summary>
    public static SrsDueItemDto RedactLocked(SrsDueItemDto dto) =>
        dto.IsLocked ? dto with { Stem = null } : dto;

    /// <summary>Blanks the mistake item stem when locked.</summary>
    public static MistakeItemDto RedactLocked(MistakeItemDto dto) =>
        dto.IsLocked ? dto with { Stem = null } : dto;
}
