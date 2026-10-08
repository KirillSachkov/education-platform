using RabbitMQ.Client.Exceptions;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ.Internal;

namespace Shared.Messaging;

/// <summary>
/// Платформенные defaults для Wolverine durability и RabbitMQ channel'ов.
///
/// Закрывает класс багов «stuck outgoing envelopes» (см. issue #20):
/// при burst publish'е Wolverine RabbitMqSender отправляет первый envelope, остальные
/// остаются в <c>wolverine_outgoing_envelopes</c> с <c>owner_id = NodeId, attempts = 0</c>
/// и не отправляются никогда (recovery sweep видит owner_id != 0 как «занято»).
///
/// Без явного <see cref="DurabilitySettings.OutboxStaleTime"/> Wolverine не освобождает ownership.
/// Wolverine docs: <see href="https://wolverinefx.net/guide/durability/index.html"/> —
/// "this should never be necessary and the Wolverine team has no clue why this could ever
/// happen and a message could get stuck, but yet, here this is".
///
/// Подключать в каждом сервисе с durable outbox:
/// <code>
/// services.AddWolverine(opts =>
/// {
///     opts.UsePlatformDurabilityDefaults();
///     opts.UseRabbitMq(connectionString)
///         .UsePlatformChannelDefaults()
///         .AutoProvision();
/// });
/// </code>
/// </summary>
public static class PlatformWolverineDefaults
{
    /// <summary>
    /// Safety net для случая когда primary sender path всё же зависает (на проде такого
    /// не наблюдали с момента перехода на <see cref="DurabilityMode.Solo"/>, но оставлено
    /// как defensive — раньше восстанавливало нас от Wolverine sender bug при
    /// Balanced mode на single-replica deployment).
    /// </summary>
    public static readonly TimeSpan DEFAULT_OUTBOX_STALE_TIME = TimeSpan.FromSeconds(5);

    /// <summary>Симметрично для durable inbox — на случай stuck incoming envelope.</summary>
    public static readonly TimeSpan DEFAULT_INBOX_STALE_TIME = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Конфигурирует <see cref="WolverineOptions.Durability"/> и <see cref="WolverineOptions.SendingFailure"/>
    /// для авто-восстановления stuck envelope'ов и graceful degradation при broker outage.
    ///
    /// Отдельно от <c>ConfigureStandardErrorPolicies</c>, потому что та работает с message-handler'ами,
    /// а здесь — про sending pipeline и sweep job'ы.
    /// </summary>
    public static void UsePlatformDurabilityDefaults(this WolverineOptions opts)
    {
        // КОРЕНЬ burst-publish bottleneck'а (issue #67): default DurabilityMode.Balanced
        // оптимизирован для multi-node deployment — на каждый envelope делает coordination
        // через wolverine_nodes (ownership assignment / healthcheck / agent re-assignment).
        // На single-replica проде это лишний overhead, который сериализует sender pipeline
        // и проявляется как «первый envelope eager, остальные ждут sweep».
        //
        // Solo mode: «All known agents will automatically start on the local node. The
        // recovered inbox/outbox messages will start functioning immediately». Никакой
        // распределённой координации — пропускная способность outbox растёт на порядок.
        //
        // Каждый сервис у нас — single docker container, без HA-репликации. Если когда-то
        // понадобится multi-replica → переключить обратно на Balanced (или поднимать
        // ноды через AssignedNodeNumber правильно). Durable guarantees сохранены: envelope'ы
        // всё так же лежат в `wolverine_outgoing_envelopes` пока broker не ack'нет.
        opts.Durability.Mode = DurabilityMode.Solo;

        opts.Durability.OutboxStaleTime = DEFAULT_OUTBOX_STALE_TIME;
        opts.Durability.InboxStaleTime = DEFAULT_INBOX_STALE_TIME;

        // Catastrophic broker outage: ставим sender'у паузу, чтобы не молотить уже мёртвый
        // канал. После 30s — следующая попытка, если broker поднялся, отправка возобновится.
        opts.SendingFailure
            .OnException<BrokerUnreachableException>()
            .PauseSending(TimeSpan.FromSeconds(30));

        // Любая прочая ошибка отправки — exponential-ish backoff. После 3-х фейлов
        // envelope уйдёт в DLQ через стандартную error policy.
        opts.SendingFailure
            .OnException<Exception>()
            .ScheduleRetry(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Включает RabbitMQ publisher confirms — sender не помечает envelope как sent, пока broker
    /// не подтвердил приём. Это убирает первопричину stuck envelope'ов (issue #20):
    /// при выключенных confirms Wolverine считает envelope ушедшим сразу после <c>BasicPublish</c>,
    /// без проверки что broker принял (и при перегруженном channel.flow off envelope «теряется»
    /// без exception).
    ///
    /// Tracking тоже включён — Wolverine ждёт ack per-message и удаляет row из
    /// <c>wolverine_outgoing_envelopes</c> только после подтверждения. Tradeoff: latency
    /// публикации растёт на round-trip до broker'а, но это правильная стоимость exactly-once.
    /// </summary>
    public static RabbitMqTransportExpression UsePlatformChannelDefaults(
        this RabbitMqTransportExpression rabbit)
    {
        return rabbit.ConfigureChannelCreation(o =>
        {
            o.PublisherConfirmationsEnabled = true;
            o.PublisherConfirmationTrackingEnabled = true;
        });
    }
}
