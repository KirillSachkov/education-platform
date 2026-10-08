using System.Collections.Concurrent;
using System.Globalization;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
/// Dedup-стор «было ли уже отправлено приветствие плана этому Telegram-юзеру в данное место».
/// Ключ — тройка (planId, <see cref="WelcomeDestination"/>, telegramUserId). Гарантирует
/// exactly-once welcome <b>per destination</b> поверх нескольких триггеров (approve join-request,
/// plain chat_member join, plan_grant.created для уже-участника, resync invite'ов), которые
/// могут сработать для одного и того же (user, plan).
///
/// Раньше ключ не включал destination (#444), поэтому раннее DM-приветствие (member-branch
/// <c>plan_grant.created</c>) подавляло позднее приветствие В ГРУППЕ при реальном входе как
/// <c>AlreadySent</c> — пользователь, принятый в группу, не получал группового приветствия.
/// Destination в ключе разводит DM и GROUP: одно не подавляет другое (#687).
///
/// Семантика «once ever per destination» — запись персистентная (без TTL): кардинальность
/// ограничена (платящие юзеры × планы × 2), поэтому накопление не проблема. Fail-open: при
/// недоступности Redis считаем «не отправлено» — лучше редкий дубль приветствия, чем подавленный
/// welcome (именно подавление welcome — баг, который этот стор чинит, #444).
///
/// Занятие ключа атомарное (set-if-absent): один вход в группу порождает два конкурентных
/// Telegram-апдейта (approve join-request + chat_member join), и неатомарная пара
/// «проверить → пометить» пропускала оба через check-then-act окно — приветствие уходило
/// дважды (прод-инцидент 2026-07-05). Победитель <see cref="TryMarkSentAsync"/> обязан
/// снять пометку через <see cref="UnmarkSentAsync"/>, если отправка не состоялась.
///
/// Реализации:
/// <list type="bullet">
/// <item><see cref="InMemoryPlanWelcomeSentStore"/> — fallback для тестов и dev'а без Redis.</item>
/// <item><c>RedisPlanWelcomeSentStore</c> (Web) — production, через <c>StackExchange.Redis</c>.</item>
/// </list>
/// </summary>
public interface IPlanWelcomeSentStore
{
    /// <summary>
    ///     Атомарно занимает ключ (set-if-absent). <c>true</c> — ключ занят этим вызовом
    ///     (приветствие можно слать), <c>false</c> — уже занят другим триггером.
    /// </summary>
    Task<bool> TryMarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct);

    /// <summary>
    ///     Снимает пометку — компенсация, когда занявший ключ триггер не смог отправить
    ///     приветствие (иначе транзиентный сбой подавил бы welcome навсегда).
    /// </summary>
    Task UnmarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct);
}

/// <summary>
/// In-memory реализация для тестов и dev'а без Redis. Не shared между репликами —
/// в multi-replica prod возможны дубли приветствия (безвредно), поэтому prod использует Redis.
/// </summary>
public sealed class InMemoryPlanWelcomeSentStore : IPlanWelcomeSentStore
{
    private readonly ConcurrentDictionary<string, byte> _sent = new(StringComparer.Ordinal);

    public Task<bool> TryMarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct) =>
        Task.FromResult(_sent.TryAdd(BuildKey(planId, telegramUserId, destination), 1));

    public Task UnmarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct)
    {
        _sent.TryRemove(BuildKey(planId, telegramUserId, destination), out _);
        return Task.CompletedTask;
    }

    public static string BuildKey(Guid planId, long telegramUserId, WelcomeDestination destination) =>
        $"tg:welcome:{planId:N}:{DestinationToken(destination)}:{telegramUserId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Стабильный короткий токен места доставки для Redis-ключа: <c>dm</c> / <c>group</c>.</summary>
    private static string DestinationToken(WelcomeDestination destination) =>
        destination == WelcomeDestination.Group ? "group" : "dm";
}
