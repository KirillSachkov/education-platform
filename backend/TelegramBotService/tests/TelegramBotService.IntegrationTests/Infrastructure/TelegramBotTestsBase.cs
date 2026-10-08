using AccessService.Contracts.HttpCommunication;
using Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Options;
using TelegramBotService.Infrastructure.Postgres;
using Wolverine.Testing;

namespace TelegramBotService.IntegrationTests.Infrastructure;

/// <summary>
/// База для integration-тестов handler'ов TelegramBotService. Поднимает минимальные dependencies
/// с реальным Postgres (через <see cref="TelegramBotTestFixture"/>) и подменённым
/// <see cref="IBotNotifier"/> (NSubstitute — чтобы не вызывать Telegram API).
///
/// Handler'ы конструируем вручную (не DI-scope-контейнер), каждый `BuildDbContext()` выдаёт
/// свежий DbContext. Это самый простой способ обойти TBF host и Wolverine pipeline в
/// handler-level тестах.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public abstract class TelegramBotTestsBase : IAsyncLifetime
{
    private readonly TelegramBotTestFixture _fixture;
    private readonly DbContextOptions<TelegramBotDbContext> _dbOptions;

    protected TelegramBotTestsBase(TelegramBotTestFixture fixture)
    {
        _fixture = fixture;
        _dbOptions = new DbContextOptionsBuilder<TelegramBotDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        BotNotifier = Substitute.For<IBotNotifier>();
        ChatApi = Substitute.For<IChatAdministrationApi>();
        OutboxCollector = new TestOutboxCollector();
        // Default — fail-open: getChatMember unconfigured → ChatMembershipChecker считает NOT_MEMBER →
        // F1 шлёт DM, F6 пытается kick. Тесты positive-кейса (юзер уже в чате) переопределяют через
        // ChatApi.GetChatMemberAsync(...).Returns(Success(MEMBER)).
        ChatApi
            .GetChatMemberAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatMemberInfo>.Failure(ChatApiErrorCode.ChatNotReachable));
        LoggerFactory = new NullLoggerFactory();
    }

    protected IBotNotifier BotNotifier { get; }
    protected IChatAdministrationApi ChatApi { get; }
    protected ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Per-test bucket для assertion'ов outbox publish'ей. Используется через
    /// <see cref="BuildOutboxService"/> в handler'ах, которые публикуют integration events.
    /// </summary>
    protected TestOutboxCollector OutboxCollector { get; }

    /// <summary>
    /// Pattern A shim над <see cref="OutboxCollector"/> — drop-in replacement
    /// для <see cref="IOutboxService"/> в тестах handler'ов.
    /// </summary>
    protected IOutboxService BuildOutboxService() => new TestOutboxService(OutboxCollector);

    /// <summary>
    /// Билдит общий <see cref="ChatMembershipChecker"/> поверх <see cref="ChatApi"/> — handler-ы
    /// (F1/F6 + GetMyChats + InviteResync) теперь делают per-binding проверку «уже в чате?».
    /// По умолчанию <see cref="ChatApi"/> возвращает default — failure-семантика, fail-open
    /// trakтуется как NOT_MEMBER → DM/kick всё равно произойдёт. Для positive-теста (юзер в чате)
    /// настройте <see cref="ChatApi"/>.GetChatMemberAsync явно.
    /// </summary>
    protected ChatMembershipChecker BuildMembershipChecker() =>
        new(ChatApi, NullLogger<ChatMembershipChecker>.Instance);

    /// <summary>
    /// <see cref="PlanWelcomeService"/> с in-memory dedup-стором и общим mock'нутым
    /// <see cref="IBotNotifier"/>. Welcome-текст резолвится через переданный
    /// <paramref name="accessClient"/> (per-test stub GetPlanTelegramInfoAsync).
    /// </summary>
    protected PlanWelcomeService BuildPlanWelcomeService(IAccessServiceClient accessClient) =>
        BuildPlanWelcomeService(accessClient, new InMemoryPlanWelcomeSentStore());

    /// <summary>
    /// Вариант с явно переданным <paramref name="sentStore"/> — нужен тестам, которым требуется
    /// пред-засеять dedup-стор (чтобы проверить, что <c>force:true</c> минует dedup-чек
    /// <see cref="IPlanWelcomeSentStore.TryMarkSentAsync"/>).
    /// </summary>
    protected PlanWelcomeService BuildPlanWelcomeService(
        IAccessServiceClient accessClient, IPlanWelcomeSentStore sentStore) =>
        new(
            accessClient,
            BotNotifier,
            sentStore,
            Options.Create(new TelegramNotificationOptions { FrontendBaseUrl = "https://example.com" }),
            NullLogger<PlanWelcomeService>.Instance);

    /// <summary>
    ///     Connection string из <see cref="TelegramBotTestFixture"/> — нужно тестам, которым
    ///     требуется построить отдельный ServiceProvider (например, для классов, использующих
    ///     <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/>).
    /// </summary>
    protected string FixtureConnectionString => _fixture.ConnectionString;

    /// <summary>
    /// Новый <see cref="TelegramBotDbContext"/> — caller отвечает за dispose.
    /// </summary>
    protected TelegramBotDbContext BuildDbContext() => new(_dbOptions);

    /// <summary>
    /// <see cref="ITransactionManager"/> поверх переданного DbContext.
    /// Минимальный stub без Wolverine outbox'а — handler'ы TelegramBotService не publish'ят
    /// integration events через outbox, так что нам достаточно SaveChangesAsync.
    /// </summary>
    protected ITransactionManager BuildTransactionManager(TelegramBotDbContext db) =>
        new TestTransactionManager(db);

    protected IUserLinkRepository BuildUserLinkRepository(TelegramBotDbContext db) =>
        new UserLinkRepository(db, BuildTransactionManager(db));

    protected async Task ExecuteInDb(Func<TelegramBotDbContext, Task> action)
    {
        await using TelegramBotDbContext db = BuildDbContext();
        await action(db);
    }

    protected async Task<T> ExecuteInDb<T>(Func<TelegramBotDbContext, Task<T>> action)
    {
        await using TelegramBotDbContext db = BuildDbContext();
        return await action(db);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        BotNotifier.ClearReceivedCalls();
        ChatApi.ClearReceivedCalls();
        OutboxCollector.Clear();
        await _fixture.ResetDatabaseAsync();
    }
}
