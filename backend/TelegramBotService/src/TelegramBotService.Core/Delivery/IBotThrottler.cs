using System.Collections.Concurrent;
using System.Diagnostics;
using TelegramBotService.Core.Diagnostics;

namespace TelegramBotService.Core.Delivery;

/// <summary>
/// Per-chat rate gate перед Telegram <c>SendMessage</c>. Telegram Bot API
/// официально лимитирует sendMessage в 1 сообщение в секунду на один chat
/// (на DM) и 20 сообщений в минуту на групповой чат — превышение даёт
/// <c>429 Too Many Requests</c> с <c>retry_after</c>.
///
/// TBF имеет global token-bucket (default 25 msg/sec), но не per-chat.
/// На массовом fan-out (один пользователь подписан на много sub-сущностей,
/// получает несколько уведомлений в секунду) можно превысить per-chat
/// лимит и получить flood control.
///
/// Вызывается из <see cref="TelegramDeliveryService.DeliverAsync"/> перед
/// каждой попыткой отправки.
/// </summary>
public interface IBotThrottler
{
    /// <summary>
    /// Ожидает разрешения на отправку в указанный <paramref name="chatId"/>.
    /// Возвращает <see cref="IAsyncDisposable"/>; <c>DisposeAsync</c> освобождает
    /// gate и фиксирует <c>lastSentAt</c> — следующий <see cref="AcquireAsync"/>
    /// для того же chat'а получит wait как минимум на оставшуюся часть
    /// 1-секундного окна.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(long chatId, CancellationToken ct);
}

/// <summary>
/// In-process per-chat throttler. Минимальный interval между sends в один
/// chat — <see cref="MinIntervalSeconds"/> (default 1s).
///
/// Memory: <see cref="ConcurrentDictionary{TKey,TValue}"/> на каждый chat.
/// При scale (тысячи активных TG-юзеров) можно вырасти; на нашем масштабе
/// (десятки–сотни user_links) приемлемо. Cleanup не реализован — entries
/// живут до restart'а сервиса.
///
/// **Важно:** работает только в пределах одной реплики. На multi-replica
/// сетапе нужно переходить на Redis-distributed rate limiter — но
/// TelegramBotService и так single-replica (polling mode), так что
/// безопасно.
/// </summary>
public sealed class PerChatBotThrottler : IBotThrottler, IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<long, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<long, DateTimeOffset> _lastSent = new();
    private readonly TelegramMetrics _metrics;

    public PerChatBotThrottler(TelegramMetrics metrics)
    {
        _metrics = metrics;
    }

    public async Task<IAsyncDisposable> AcquireAsync(long chatId, CancellationToken ct)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        SemaphoreSlim sem = _locks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_lastSent.TryGetValue(chatId, out DateTimeOffset last))
            {
                TimeSpan elapsed = DateTimeOffset.UtcNow - last;
                if (elapsed < MinInterval)
                {
                    TimeSpan wait = MinInterval - elapsed;
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                }
            }

            // Включает и semaphore-wait (concurrent send в один chat), и 1s throttle gate.
            // Высокий p95 → бутылочное горло на параллельной доставке.
            _metrics.RecordThrottleWait(Stopwatch.GetElapsedTime(startTimestamp));

            return new SendToken(sem, _lastSent, chatId);
        }
        catch
        {
            sem.Release();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (SemaphoreSlim sem in _locks.Values)
            sem.Dispose();
        _locks.Clear();
    }

    private sealed class SendToken : IAsyncDisposable
    {
        private readonly SemaphoreSlim _sem;
        private readonly ConcurrentDictionary<long, DateTimeOffset> _lastSent;
        private readonly long _chatId;
        private bool _disposed;

        public SendToken(SemaphoreSlim sem, ConcurrentDictionary<long, DateTimeOffset> lastSent, long chatId)
        {
            _sem = sem;
            _lastSent = lastSent;
            _chatId = chatId;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            _lastSent[_chatId] = DateTimeOffset.UtcNow;
            _sem.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// No-op throttler для тестов и cases где per-chat throttling не нужен.
/// </summary>
public sealed class NoopBotThrottler : IBotThrottler
{
    private static readonly IAsyncDisposable _emptyToken = new EmptyToken();

    public Task<IAsyncDisposable> AcquireAsync(long chatId, CancellationToken ct) =>
        Task.FromResult(_emptyToken);

    private sealed class EmptyToken : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
