using System.Collections.Concurrent;

namespace Wolverine.Testing;

/// <summary>
///     Записывает все integration-event'ы, опубликованные через тестовый
///     <c>IOutboxService</c>. Регистрируется как singleton в test factory; per-service
///     TestOutboxService-обёртка делегирует сюда. Тесты ассертят публикации через
///     <see cref="Messages"/> + <see cref="OfType{T}"/>.
/// </summary>
/// <remarks>
///     Семантика та же, что у Wolverine <c>TrackActivity().Sent</c>, но без зависимости
///     от durable envelope-storage. На pattern-B сервисах (CommentService, FileService,
///     AccessService) durability-agent даёт race с <c>Respawn.ResetAsync</c> → 40P01;
///     этот collector полностью убирает durability из тестового пути.
/// </remarks>
public sealed class TestOutboxCollector
{
    private readonly ConcurrentQueue<object> _messages = new();

    /// <summary>
    ///     Snapshot всех опубликованных сообщений в порядке публикации.
    /// </summary>
    public IReadOnlyCollection<object> Messages => _messages.ToArray();

    public void Add(object message) => _messages.Enqueue(message);

    /// <summary>
    ///     Очищает collector. Вызывается из <c>ResetDatabaseAsync</c> перед каждым тестом —
    ///     иначе сообщения из предыдущего теста утекут в новый.
    /// </summary>
    public void Clear()
    {
        while (_messages.TryDequeue(out _))
        {
            // drain
        }
    }

    /// <summary>
    ///     Удобный фильтр по типу для assert'ов: <c>collector.OfType&lt;MaterialCreated&gt;().Single()</c>.
    /// </summary>
    public IEnumerable<T> OfType<T>() where T : class => _messages.OfType<T>();
}
