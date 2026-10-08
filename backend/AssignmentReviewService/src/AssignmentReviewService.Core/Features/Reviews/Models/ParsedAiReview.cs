using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Features.Reviews.Models;

/// <summary>
///     Результат прогона AI-pipeline'а: типизированный verdict + summary +
///     inline-комментарии готовые к публикации в GitHub. На uplevel'е к
///     <see cref="AiReviewResponseDto"/> добавлен parsed verdict + token usage.
///     Поля #798: <see cref="NeedFiles"/> — сырой запрос файлов из ЭТОГО ответа
///     (потребляется циклом дозапроса); <see cref="RequestedFiles"/> /
///     <see cref="ContextRounds"/> — итоговая телеметрия цикла на финальном результате.
/// </summary>
public sealed record ParsedAiReview(
    AiReviewVerdict Verdict,
    string Summary,
    IReadOnlyList<VcsReviewComment> InlineComments,
    string ModelUsed,
    int? InputTokens,
    int? OutputTokens,
    IReadOnlyList<string>? NeedFiles = null,
    IReadOnlyList<string>? RequestedFiles = null,
    int ContextRounds = 0);
