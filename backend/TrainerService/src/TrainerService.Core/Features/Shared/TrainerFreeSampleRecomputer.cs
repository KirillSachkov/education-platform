using Microsoft.Extensions.Options;
using TrainerService.Core.Configuration;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Shared;

/// <summary>
///     Orchestrates the per-topic free-sample recompute (#674): loads the topic's STUDY-bank questions,
///     runs <see cref="TrainerFreeAllocationPolicy.Recompute"/>, and mutates
///     the tracked aggregates. The CALLER owns the transaction — this only mutates, the handler's
///     <c>SaveChangesAsync</c> persists the flag changes atomically with its own write.
///     <para>
///         Called after every topic-composition mutation (create / update / delete question) and by
///         the <c>recompute-free-samples</c> backfill CLI + the seeder. Reading the question's bank for
///         the topic id keeps it agnostic to which mutation triggered it.
///     </para>
/// </summary>
public sealed class TrainerFreeSampleRecomputer
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly TrainerOptions _options;

    public TrainerFreeSampleRecomputer(
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        IOptions<TrainerOptions> options)
    {
        _banks = banks;
        _questions = questions;
        _options = options.Value;
    }

    /// <summary>
    ///     Recomputes the free-sample allocation for the topic that owns <paramref name="bankId"/>.
    /// </summary>
    /// <param name="pendingAdd">
    ///     A just-created question not yet persisted (so a DB query can't see it) — included in the set.
    /// </param>
    /// <param name="removedQuestionId">A question being deleted in this transaction — excluded from the set.</param>
    public async Task RecomputeForBankAsync(
        Guid bankId,
        TrainerQuestion? pendingAdd = null,
        Guid? removedQuestionId = null,
        CancellationToken ct = default)
    {
        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == bankId, ct);
        if (bankResult.IsFailure)
            return; // bank gone — nothing to recompute.

        await RecomputeForTopicAsync(bankResult.Value.TopicId, pendingAdd, removedQuestionId, ct);
    }

    /// <summary>
    ///     Recomputes the free-sample allocation across a topic's STUDY banks only. MOCK banks are
    ///     excluded: mock content is hard PRO-gated (never a free sample), and every access gate that
    ///     reads <see cref="TrainerQuestion.IsFreeSample"/> looks at STUDY banks — allocating free slots
    ///     to mock questions would starve study questions and mis-lock the topic (#674, code-review SF-1).
    /// </summary>
    public async Task RecomputeForTopicAsync(
        Guid topicId,
        TrainerQuestion? pendingAdd = null,
        Guid? removedQuestionId = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<TopicBank> banks = await _banks.GetManyByAsync(
            b => b.TopicId == topicId && b.Purpose == BankPurpose.STUDY, ct);
        HashSet<Guid> bankIds = banks.Select(b => b.Id).ToHashSet();

        IReadOnlyList<TrainerQuestion> persisted = bankIds.Count == 0
            ? []
            : await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), ct);

        List<TrainerQuestion> set = persisted
            .Where(q => removedQuestionId is null || q.Id != removedQuestionId)
            .ToList();

        // A pending question participates only if it lands in a STUDY bank — a freshly-created MOCK
        // question is never a free sample.
        if (pendingAdd is not null
            && bankIds.Contains(pendingAdd.BankId)
            && set.TrueForAll(q => q.Id != pendingAdd.Id))
        {
            set.Add(pendingAdd);
        }

        TrainerFreeAllocationPolicy.Recompute(set, _options.FreeSamplePercent);
    }
}
