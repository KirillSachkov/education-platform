/**
 * Multi-tab leader election + cross-tab pub/sub. Generic-helper для дедупликации
 * любых per-user resource'ов (SSE, polling, websocket'ы), которые иначе бы
 * открывались независимо в каждой вкладке.
 *
 * Два инструмента:
 * 1. **Web Locks API** (`navigator.locks`) — браузер-native mutex. Только одна
 *    tab держит exclusive lock с заданным именем. На закрытие tab — lock
 *    авто-освобождается → одна из waiting tabs становится новым leader'ом.
 *    Никаких manual heartbeat'ов.
 *
 * 2. **BroadcastChannel** — leader публикует обновления через named channel,
 *    остальные tabs слушают и применяют у себя.
 *
 * **Browser support:** Web Locks — Chrome 69+, Firefox 96+, Safari 15.4+
 * (≥95% совр. браузеров). BroadcastChannel — Chrome 54+, Firefox 38+,
 * Safari 15.4+. Старые браузеры graceful'но degrade'ят: каждая tab работает
 * как leader (старое поведение, никаких ломок).
 *
 * Использование:
 * - SSE notifications: `acquireLeader("sse-notifications", openSse)`
 * - AI-jobs polling: `acquireLeader("ai-jobs-poll", startPolling)`
 * - и т.д.
 */

/**
 * Поддерживает ли браузер Web Locks API.
 */
export function isWebLocksSupported(): boolean {
  return typeof navigator !== "undefined" && "locks" in navigator;
}

/**
 * Поддерживает ли браузер BroadcastChannel.
 */
export function isBroadcastChannelSupported(): boolean {
  return typeof BroadcastChannel !== "undefined";
}

/**
 * Acquire-and-hold exclusive lock с заданным именем. `onAcquired` вызывается
 * когда tab стала leader'ом — должна вернуть cleanup-функцию (которая закроет
 * leader-side ресурс). Cleanup автоматически вызывается при teardown.
 *
 * Если Web Locks не поддерживается — fallback: `onAcquired` вызывается сразу,
 * tab ведёт себя как leader без координации (старое per-tab поведение).
 *
 * @param lockName Имя lock'а (namespace через `edu-platform-{name}`).
 * @param onAcquired Callback при получении lock'а; возвращает cleanup.
 * @returns Teardown функция: освободит lock + вызовет cleanup.
 */
export function acquireLeader(
  lockName: string,
  onAcquired: () => () => void,
): () => void {
  if (!isWebLocksSupported()) {
    const cleanup = onAcquired();
    return cleanup;
  }

  const fullLockName = `edu-platform-${lockName}`;
  const abortController = new AbortController();
  let cleanup: (() => void) | null = null;

  void navigator.locks
    .request(
      fullLockName,
      { mode: "exclusive", signal: abortController.signal },
      () =>
        new Promise<void>((resolve) => {
          cleanup = onAcquired();
          abortController.signal.addEventListener("abort", () => resolve(), {
            once: true,
          });
        }),
    )
    .catch((error) => {
      if (error instanceof DOMException && error.name === "AbortError") return;
      if (process.env.NODE_ENV === "development") {
        console.warn(`[leader-election] lock '${fullLockName}' failed:`, error);
      }
    });

  return () => {
    abortController.abort();
    cleanup?.();
  };
}

/**
 * Открыть BroadcastChannel для cross-tab сообщений. Channel name namespace'ится
 * `edu-platform-{name}`. Generic over типом сообщений — caller specifies через
 * type parameter.
 *
 * Возвращает `{ post, close }`. На non-supporting браузерах оба — noop.
 *
 * **Note:** BroadcastChannel НЕ доставляет сообщения в ту же tab, откуда они
 * posted. Так что leader, выпускающий update в channel, должен отдельно
 * применить его локально — broadcast только для других tabs.
 */
export function openChannel<T>(channelName: string): {
  post: (msg: T) => void;
  close: () => void;
} {
  if (!isBroadcastChannelSupported()) {
    return { post: () => {}, close: () => {} };
  }
  const channel = new BroadcastChannel(`edu-platform-${channelName}`);
  return {
    post: (msg) => channel.postMessage(msg),
    close: () => channel.close(),
  };
}

/**
 * Подписаться на BroadcastChannel сообщения. Listener вызывается на каждое
 * сообщение от других tabs. Возвращает teardown.
 *
 * Channel name тот же что в `openChannel` — это pub/sub topic, не singleton:
 * можно открывать сколько угодно instances с одним именем, они все
 * connected к одной channel-topology.
 */
export function subscribeChannel<T>(
  channelName: string,
  listener: (msg: T) => void,
): () => void {
  if (!isBroadcastChannelSupported()) {
    return () => {};
  }
  const channel = new BroadcastChannel(`edu-platform-${channelName}`);
  const handler = (event: MessageEvent<T>) => listener(event.data);
  channel.addEventListener("message", handler);
  return () => {
    channel.removeEventListener("message", handler);
    channel.close();
  };
}
