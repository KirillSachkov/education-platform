using System.Reflection;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Core.Database;
using Microsoft.Extensions.Logging;
using Ordering;
using SharedKernel;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;
using TrainerService.Domain.Tracks;

namespace TrainerService.Web.Configuration;

/// <summary>
///     Содержимое seed-файла <c>SeedData/trainer-seed.json</c> (embedded resource) —
///     таксономия тренажёра: треки → темы → (опц.) банки + (опц.) именованные мок-собесы.
///     Маппинг идёт через те же доменные фабрики, что и author-API, так что валидация переиспользуется.
/// </summary>
public sealed record TrainerSeedDocument(
    IReadOnlyList<TrackSeed> Tracks,
    IReadOnlyList<MockInterviewSeed>? MockInterviews = null);

/// <summary>Трек (язык/стек) + его темы. Создаётся опубликованным.</summary>
public sealed record TrackSeed(
    string? Slug,
    string? Title,
    string? Stack,
    string? Description,
    IReadOnlyList<TopicSeed>? Topics);

/// <summary>Тема трека + (опц.) её банки вопросов. Создаётся опубликованной.</summary>
public sealed record TopicSeed(
    string? Slug,
    string? Title,
    string? Area,
    string? Description,
    string? Direction,
    IReadOnlyList<BankSeed>? Banks);

/// <summary>
///     Банк темы с inline-вопросами (собственный банк тренажёра, #623). Идемпотентность по
///     паре <c>(TopicId, Tier, Purpose)</c>: банк с уже залитыми вопросами не трогается (skip),
///     <c>--force</c> пере-создаёт его вопросы. Опционально.
/// </summary>
public sealed record BankSeed(
    string? Tier,
    string? Difficulty,
    string? Purpose,
    IReadOnlyList<QuestionSeed>? Questions);

/// <summary>Вопрос банка (inline в seed-файле). Варианты — только для choice-типов.</summary>
public sealed record QuestionSeed(
    string? Stem,
    string? Type,
    string? ReferenceAnswer,
    string? Explanation,
    string? Difficulty,
    string? Section,
    IReadOnlyList<OptionSeed>? Options);

/// <summary>Вариант ответа вопроса (для SINGLE/MULTI_CHOICE).</summary>
public sealed record OptionSeed(string? Text, bool IsCorrect);

/// <summary>
///     Именованная симуляция собеса в seed-файле. Курированный набор собирается из вопросов
///     перечисленных тем (<see cref="TopicSlugs"/>) — без жёстких question-id в JSON, чтобы seed
///     переживал ре-генерацию id. Сидер резолвит slug'и тем → их вопросы → курированный набор +
///     <see cref="QuestionsPerSession"/>. Идемпотентность по <c>Slug</c>. Создаётся опубликованной.
/// </summary>
public sealed record MockInterviewSeed(
    string? Slug,
    string? Title,
    string? Description,
    int? QuestionsPerSession,
    IReadOnlyList<string>? TopicSlugs);

/// <summary>Что сделал сидер с конкретной сущностью.</summary>
public enum TrainerSeedAction
{
    /// <summary>Сущности не было — создана из seed-файла.</summary>
    CREATED,

    /// <summary>Сущность была — обновлены мутабельные метаданные (только с <c>--force</c>).</summary>
    UPDATED,

    /// <summary>Сущность была — не тронута (дефолт).</summary>
    SKIPPED_EXISTING,
}

/// <summary>Сводка сидинга: счётчики по сущностям.</summary>
public sealed record TrainerSeedResult(
    int TracksCreated,
    int TracksUpdated,
    int TracksSkipped,
    int TopicsCreated,
    int TopicsUpdated,
    int TopicsSkipped,
    int BanksCreated,
    int BanksSkipped,
    int QuestionsCreated);

/// <summary>
///     Идемпотентный сидер таксономии тренажёра (треки → темы → банки) из embedded-файла
///     <c>SeedData/trainer-seed.json</c>. Ядро CLI-команды <c>seed-trainer</c>
///     (<see cref="SeedTrainerCli"/>), вынесено в отдельный класс ради тестируемости.
///     <para>
///     <b>Идемпотентность:</b> треки матчатся по <c>Slug</c>, темы — по паре
///     <c>(TrackId, Slug)</c>, банки — по тройке <c>(TopicId, Tier, Purpose)</c>. Уже существующая
///     сущность по умолчанию НЕ трогается (skip). С <c>force: true</c> у трека/темы обновляются
///     мутабельные метаданные (title/area/description/direction/stack); банк с уже залитыми
///     вопросами пере-создаёт свои вопросы (старые сносятся), пустой банк просто доливается.
///     </para>
///     <para>
///     Всё через домен (фабрики + методы агрегата) — никакого raw SQL. Вопросы создаются как
///     <see cref="TrainerQuestion"/>-агрегаты собственного банка тренажёра (#623), без ECS.
///     Сохранение — через <see cref="ITransactionManager"/>, чтобы доменные события корректно диспетчились.
///     </para>
/// </summary>
public sealed class TrainerSeeder
{
    private const string SEED_RESOURCE_NAME = "TrainerService.Web.SeedData.trainer-seed.json";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITracksRepository _tracks;
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<TrainerSeeder> _logger;

    public TrainerSeeder(
        ITracksRepository tracks,
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        IMockInterviewsRepository mockInterviews,
        ITransactionManager transactions,
        ILogger<TrainerSeeder> logger)
    {
        _tracks = tracks;
        _topics = topics;
        _banks = banks;
        _questions = questions;
        _mockInterviews = mockInterviews;
        _transactions = transactions;
        _logger = logger;
    }

    /// <summary>Читает и десериализует seed-файл из embedded resource сборки.</summary>
    public static TrainerSeedDocument LoadSeedDocument()
    {
        Assembly assembly = typeof(TrainerSeeder).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(SEED_RESOURCE_NAME)
            ?? throw new InvalidOperationException($"Embedded resource '{SEED_RESOURCE_NAME}' not found.");

        return JsonSerializer.Deserialize<TrainerSeedDocument>(stream, _jsonOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{SEED_RESOURCE_NAME}' deserialized to null.");
    }

    public async Task<Result<TrainerSeedResult, Error>> SeedAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        TrainerSeedDocument document = LoadSeedDocument();

        int tracksCreated = 0, tracksUpdated = 0, tracksSkipped = 0;
        int topicsCreated = 0, topicsUpdated = 0, topicsSkipped = 0;
        int banksCreated = 0, banksSkipped = 0, questionsCreated = 0;

        foreach (TrackSeed trackSeed in document.Tracks)
        {
            Result<(Track Track, TrainerSeedAction Action), Error> trackResult =
                await UpsertTrackAsync(trackSeed, force, cancellationToken);
            if (trackResult.IsFailure)
                return trackResult.Error;

            (Track track, TrainerSeedAction trackAction) = trackResult.Value;
            switch (trackAction)
            {
                case TrainerSeedAction.CREATED: tracksCreated++; break;
                case TrainerSeedAction.UPDATED: tracksUpdated++; break;
                default: tracksSkipped++; break;
            }

            foreach (TopicSeed topicSeed in trackSeed.Topics ?? [])
            {
                Result<(Topic Topic, TrainerSeedAction Action), Error> topicResult =
                    await UpsertTopicAsync(track.Id, topicSeed, force, cancellationToken);
                if (topicResult.IsFailure)
                    return topicResult.Error;

                (Topic topic, TrainerSeedAction topicAction) = topicResult.Value;
                switch (topicAction)
                {
                    case TrainerSeedAction.CREATED: topicsCreated++; break;
                    case TrainerSeedAction.UPDATED: topicsUpdated++; break;
                    default: topicsSkipped++; break;
                }

                foreach (BankSeed bankSeed in topicSeed.Banks ?? [])
                {
                    Result<(bool Created, int Questions), Error> bankResult =
                        await UpsertBankAsync(topic.Id, bankSeed, force, cancellationToken);
                    if (bankResult.IsFailure)
                        return bankResult.Error;

                    if (bankResult.Value.Created)
                        banksCreated++;
                    else
                        banksSkipped++;

                    questionsCreated += bankResult.Value.Questions;
                }
            }
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Пасс 2: именованные мок-собесы. Идёт ПОСЛЕ save вопросов — резолв курированного набора
        // (slug темы → её вопросы) читает их уже из БД, а не из незакоммиченного change-tracker'а.
        Result<(int Created, int Updated, int Skipped), Error> mockResult =
            await SeedMockInterviewsAsync(document.MockInterviews ?? [], force, cancellationToken);
        if (mockResult.IsFailure)
            return mockResult.Error;

        UnitResult<Error> mockSaveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (mockSaveResult.IsFailure)
            return mockSaveResult.Error;

        var result = new TrainerSeedResult(
            tracksCreated, tracksUpdated, tracksSkipped,
            topicsCreated, topicsUpdated, topicsSkipped,
            banksCreated, banksSkipped, questionsCreated);

        _logger.LogInformation(
            "seed-trainer done: tracks +{TracksCreated}/~{TracksUpdated}/={TracksSkipped}, " +
            "topics +{TopicsCreated}/~{TopicsUpdated}/={TopicsSkipped}, banks +{BanksCreated}/={BanksSkipped}, " +
            "questions +{QuestionsCreated}, mock-interviews +{MocksCreated}/~{MocksUpdated}/={MocksSkipped}",
            result.TracksCreated, result.TracksUpdated, result.TracksSkipped,
            result.TopicsCreated, result.TopicsUpdated, result.TopicsSkipped,
            result.BanksCreated, result.BanksSkipped, result.QuestionsCreated,
            mockResult.Value.Created, mockResult.Value.Updated, mockResult.Value.Skipped);

        return result;
    }

    private async Task<Result<(Track, TrainerSeedAction), Error>> UpsertTrackAsync(
        TrackSeed seed, bool force, CancellationToken ct)
    {
        if (!TryParseEnum(seed.Stack, out TrackStack stack))
            return TrainerServiceErrors.Track.InvalidStack(seed.Stack ?? string.Empty);

        string slug = (seed.Slug ?? string.Empty).Trim();
        IReadOnlyList<Track> matches = await _tracks.GetManyByAsync(t => t.Slug == slug, ct);
        Track? existing = matches.Count > 0 ? matches[0] : null;

        if (existing is not null)
        {
            if (!force)
                return (existing, TrainerSeedAction.SKIPPED_EXISTING);

            UnitResult<Error> updateResult = existing.UpdateDetails(seed.Title, stack, seed.Description);
            if (updateResult.IsFailure)
                return updateResult.Error;

            return (existing, TrainerSeedAction.UPDATED);
        }

        string sortKey = await ComputeAppendTrackSortKeyAsync(ct);
        Result<Track, Error> created = Track.Create(seed.Slug, seed.Title, stack, seed.Description, sortKey);
        if (created.IsFailure)
            return created.Error;

        Track track = created.Value;
        track.Publish();
        await _tracks.AddAsync(track, ct);
        return (track, TrainerSeedAction.CREATED);
    }

    private async Task<Result<(Topic, TrainerSeedAction), Error>> UpsertTopicAsync(
        Guid trackId, TopicSeed seed, bool force, CancellationToken ct)
    {
        TopicDirection? direction = null;
        if (!string.IsNullOrWhiteSpace(seed.Direction))
        {
            if (!TryParseEnum(seed.Direction, out TopicDirection parsedDirection))
                return TrainerServiceErrors.Topic.InvalidDirection(seed.Direction);

            direction = parsedDirection;
        }

        string slug = (seed.Slug ?? string.Empty).Trim();
        IReadOnlyList<Topic> matches = await _topics.GetManyByAsync(t => t.TrackId == trackId && t.Slug == slug, ct);
        Topic? existing = matches.Count > 0 ? matches[0] : null;

        if (existing is not null)
        {
            if (!force)
                return (existing, TrainerSeedAction.SKIPPED_EXISTING);

            UnitResult<Error> detailsResult = existing.UpdateDetails(seed.Title, seed.Area, seed.Description);
            if (detailsResult.IsFailure)
                return detailsResult.Error;

            UnitResult<Error> trackResult = existing.SetTrackAndDirection(trackId, direction);
            if (trackResult.IsFailure)
                return trackResult.Error;

            return (existing, TrainerSeedAction.UPDATED);
        }

        string sortKey = await ComputeAppendTopicSortKeyAsync(ct);
        Result<Topic, Error> created = Topic.Create(
            trackId, seed.Slug, seed.Title, seed.Area, seed.Description, direction, sortKey);
        if (created.IsFailure)
            return created.Error;

        Topic topic = created.Value;
        topic.Publish();
        await _topics.AddAsync(topic, ct);
        return (topic, TrainerSeedAction.CREATED);
    }

    /// <summary>
    ///     Создаёт/доливает банк темы по тройке <c>(TopicId, Tier, Purpose)</c> + его inline-вопросы (#623).
    ///     Если такой банк уже существует и в нём есть вопросы — skip (без <c>--force</c>); с <c>--force</c>
    ///     старые вопросы сносятся и пере-создаются. Пустой существующий банк доливается вопросами.
    ///     Возвращает (был ли создан новый банк, сколько вопросов создано).
    /// </summary>
    private async Task<Result<(bool Created, int Questions), Error>> UpsertBankAsync(
        Guid topicId, BankSeed seed, bool force, CancellationToken ct)
    {
        BankTier tier = TryParseEnum(seed.Tier, out BankTier parsedTier) ? parsedTier : BankTier.FREE;
        BankPurpose purpose = TryParseEnum(seed.Purpose, out BankPurpose parsedPurpose) ? parsedPurpose : BankPurpose.STUDY;
        QuestionDifficulty? bankDifficulty =
            TryParseEnum(seed.Difficulty, out QuestionDifficulty parsedDifficulty) ? parsedDifficulty : null;

        IReadOnlyList<TopicBank> topicBanks = await _banks.GetManyByAsync(b => b.TopicId == topicId, ct);
        TopicBank? bank = topicBanks.FirstOrDefault(b => b.Tier == tier && b.Purpose == purpose);

        bool created = false;
        if (bank is null)
        {
            string sortKey = await ComputeAppendBankSortKeyAsync(topicId, ct);
            Result<TopicBank, Error> createdBank = TopicBank.Create(topicId, tier, bankDifficulty, sortKey, purpose);
            if (createdBank.IsFailure)
                return createdBank.Error;

            bank = createdBank.Value;
            await _banks.AddAsync(bank, ct);
            created = true;
        }
        else
        {
            // Существующий банк с вопросами не трогаем (без --force); с --force сносим вопросы.
            int existingCount = await _questions.CountByAsync(q => q.BankId == bank.Id, ct);
            if (existingCount > 0)
            {
                if (!force)
                    return (false, 0);

                IReadOnlyList<TrainerQuestion> existing =
                    await _questions.GetManyByAsync(q => q.BankId == bank.Id, ct);
                foreach (TrainerQuestion q in existing)
                    await _questions.RemoveAsync(q, ct);
            }
        }

        int questionsCreated = await CreateQuestionsAsync(bank.Id, seed.Questions ?? [], ct);
        return (created, questionsCreated);
    }

    /// <summary>Создаёт вопросы банка из seed-файла (через доменную фабрику). Возвращает число созданных.</summary>
    private async Task<int> CreateQuestionsAsync(Guid bankId, IReadOnlyList<QuestionSeed> seeds, CancellationToken ct)
    {
        int created = 0;
        for (int index = 0; index < seeds.Count; index++)
        {
            QuestionSeed seed = seeds[index];

            if (!TryParseEnum(seed.Type, out TrainerQuestionType type))
                return created; // невалидный тип в seed — стоп на этом банке (контент seed-файла битый).

            QuestionDifficulty? difficulty =
                TryParseEnum(seed.Difficulty, out QuestionDifficulty parsedDifficulty) ? parsedDifficulty : null;

            IReadOnlyList<(string Text, bool IsCorrect)> options = (seed.Options ?? [])
                .Select(o => (o.Text ?? string.Empty, o.IsCorrect))
                .ToList();

            Result<TrainerQuestion, Error> question = TrainerQuestion.Create(
                bankId,
                seed.Stem,
                type,
                seed.ReferenceAnswer,
                seed.Explanation,
                difficulty,
                seed.Section,
                IndexedSortKey(index),
                options);
            if (question.IsFailure)
                return created; // невалидный вопрос — стоп (битый seed-файл, фейлим мягко).

            await _questions.AddAsync(question.Value, ct);
            created++;
        }

        return created;
    }

    /// <summary>
    ///     Сидит именованные мок-собесы из seed-файла (пасс 2). Идемпотентность по <c>Slug</c>:
    ///     существующий без <c>--force</c> — skip; с <c>--force</c> пере-собирается курированный набор.
    ///     Курированный набор = все вопросы тем, указанных в <c>topicSlugs</c> (резолв slug → темы → их
    ///     банки → вопросы); студенту на сессию выдаётся случайная подвыборка <c>questionsPerSession</c>.
    /// </summary>
    private async Task<Result<(int Created, int Updated, int Skipped), Error>> SeedMockInterviewsAsync(
        IReadOnlyList<MockInterviewSeed> seeds, bool force, CancellationToken ct)
    {
        int created = 0, updated = 0, skipped = 0;
        foreach (MockInterviewSeed seed in seeds)
        {
            Result<TrainerSeedAction, Error> actionResult = await UpsertMockInterviewAsync(seed, force, ct);
            if (actionResult.IsFailure)
                return actionResult.Error;

            switch (actionResult.Value)
            {
                case TrainerSeedAction.CREATED: created++; break;
                case TrainerSeedAction.UPDATED: updated++; break;
                default: skipped++; break;
            }
        }

        return (created, updated, skipped);
    }

    private async Task<Result<TrainerSeedAction, Error>> UpsertMockInterviewAsync(
        MockInterviewSeed seed, bool force, CancellationToken ct)
    {
        string slug = (seed.Slug ?? string.Empty).Trim();

        // Резолв тем по slug → их id + курированный набор вопросов из их банков (любого назначения).
        HashSet<string> topicSlugs = (seed.TopicSlugs ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<Topic> topics = topicSlugs.Count == 0
            ? []
            : await _topics.GetManyByAsync(t => topicSlugs.Contains(t.Slug), ct);
        List<Guid> topicIds = topics.Select(t => t.Id).ToList();

        List<Guid> questionIds = await ResolveTopicQuestionIdsAsync(topicIds, ct);

        IReadOnlyList<MockInterview> matches = await _mockInterviews.GetManyByAsync(m => m.Slug == slug, ct);
        MockInterview? existing = matches.Count > 0 ? matches[0] : null;

        if (existing is not null)
        {
            if (!force)
                return TrainerSeedAction.SKIPPED_EXISTING;

            UnitResult<Error> detailsResult = existing.UpdateDetails(seed.Title, seed.Description);
            if (detailsResult.IsFailure)
                return detailsResult.Error;

            existing.SetTopics(topicIds);
            existing.SetQuestions(questionIds);

            UnitResult<Error> perSessionResult = existing.SetQuestionsPerSession(seed.QuestionsPerSession);
            if (perSessionResult.IsFailure)
                return perSessionResult.Error;

            UnitResult<Error> publishResult = existing.Publish();
            if (publishResult.IsFailure)
                return publishResult.Error;

            return TrainerSeedAction.UPDATED;
        }

        int sortIndex = await ComputeAppendMockSortIndexAsync(ct);
        Result<MockInterview, Error> created =
            MockInterview.Create(seed.Slug, seed.Title, seed.Description, topicIds, sortIndex);
        if (created.IsFailure)
            return created.Error;

        MockInterview interview = created.Value;
        interview.SetQuestions(questionIds);

        UnitResult<Error> setPerSession = interview.SetQuestionsPerSession(seed.QuestionsPerSession);
        if (setPerSession.IsFailure)
            return setPerSession.Error;

        UnitResult<Error> publish = interview.Publish();
        if (publish.IsFailure)
            return publish.Error;

        await _mockInterviews.AddAsync(interview, ct);
        return TrainerSeedAction.CREATED;
    }

    /// <summary>Все вопросы банков указанных тем (для курированного набора мок-собеса).</summary>
    private async Task<List<Guid>> ResolveTopicQuestionIdsAsync(IReadOnlyList<Guid> topicIds, CancellationToken ct)
    {
        if (topicIds.Count == 0)
            return [];

        var topicIdSet = topicIds.ToHashSet();
        IReadOnlyList<TopicBank> banks = await _banks.GetManyByAsync(b => topicIdSet.Contains(b.TopicId), ct);
        HashSet<Guid> bankIds = banks.Select(b => b.Id).ToHashSet();
        if (bankIds.Count == 0)
            return [];

        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), ct);
        return questions.Select(q => q.Id).ToList();
    }

    private async Task<int> ComputeAppendMockSortIndexAsync(CancellationToken ct)
    {
        IReadOnlyList<MockInterview> existing = await _mockInterviews.GetManyByAsync(_ => true, ct);
        return existing.Count == 0 ? 0 : existing.Max(m => m.SortIndex) + 1;
    }

    /// <summary>Монотонный append-ключ по индексу: ключ_0, ключ_1, ... через последовательные After.</summary>
    private static string IndexedSortKey(int index)
    {
        SortKey key = SortKey.Initial();
        for (int i = 0; i < index; i++)
            key = SortKey.After(key);

        return key.Value;
    }

    private async Task<string> ComputeAppendTrackSortKeyAsync(CancellationToken ct)
    {
        IReadOnlyList<Track> existing = await _tracks.GetManyByAsync(_ => true, ct);
        return ComputeAppendSortKey(existing.Select(t => t.SortKey));
    }

    private async Task<string> ComputeAppendTopicSortKeyAsync(CancellationToken ct)
    {
        IReadOnlyList<Topic> existing = await _topics.GetManyByAsync(_ => true, ct);
        return ComputeAppendSortKey(existing.Select(t => t.SortKey));
    }

    private async Task<string> ComputeAppendBankSortKeyAsync(Guid topicId, CancellationToken ct)
    {
        IReadOnlyList<TopicBank> existing = await _banks.GetManyByAsync(b => b.TopicId == topicId, ct);
        return ComputeAppendSortKey(existing.Select(b => b.SortKey));
    }

    private static string ComputeAppendSortKey(IEnumerable<string> existingKeys)
    {
        List<string> keys = [.. existingKeys];
        if (keys.Count == 0)
            return SortKey.Initial().Value;

        string maxKey = keys.Max()!;
        Result<SortKey, Error> parsed = SortKey.Create(maxKey);
        return parsed.IsSuccess ? SortKey.After(parsed.Value).Value : SortKey.Initial().Value;
    }

    /// <summary>Строгий парс enum-члена (UPPER_SNAKE, case-sensitive). Пусто/невалид → false.</summary>
    private static bool TryParseEnum<TEnum>(string? raw, out TEnum value)
        where TEnum : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw.Trim(), ignoreCase: false, out value)
            && Enum.IsDefined(value))
        {
            return true;
        }

        value = default;
        return false;
    }
}
