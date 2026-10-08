"use client";

import {
  forwardRef,
  useEffect,
  useId,
  useImperativeHandle,
  useRef,
  useState,
  type Ref,
} from "react";
import { cn } from "@/shared/lib/css";

// Native Kinescope iframe Player API: https://docs.kinescope.com/player/latest/embed/iframe-api/
// Старый wrapper `@kinescope/react-kinescope-player@0.5.4` ломался в React 19 +
// Next.js 16 dynamic(ssr:false): фрагильный class-component с imperative DOM в
// constructor + componentDidUpdate иногда не успевал postMessage handshake'нуться
// за 7 сек до того, как React пересоздавал iframe → 7000ms timeout + "IFrame
// already removed". См. #156 (временный hotfix), #173 (прод-блокер), #157 (фон).
// Здесь — фундаментальный фикс: один React-узел как контейнер, цепляем плеер
// в useEffect, явный destroy в cleanup.

type KinescopePlayerEvents = {
  Loaded: string;
  TimeUpdate: string;
  SeekChapter: string;
  Error: string;
  Destroy: string;
};

type KinescopePlayerInstance = {
  destroy: () => Promise<void>;
  seekTo: (seconds: number) => Promise<void>;
  on: (event: string, handler: (event: { data?: unknown }) => void) => unknown;
  off: (event: string, handler: (event: { data?: unknown }) => void) => unknown;
  Events: KinescopePlayerEvents;
};

type KinescopePlayerFactory = {
  create: (elementId: string, options: Record<string, unknown>) => Promise<KinescopePlayerInstance>;
};

declare global {
  interface Window {
    Kinescope?: { IframePlayer?: KinescopePlayerFactory };
    onKinescopeIframeAPIReady?: (factory: KinescopePlayerFactory) => void;
  }
}

const SCRIPT_ID = "__kinescope_iframe_api__";
const SCRIPT_SRC = "https://player.kinescope.io/latest/iframe.player.js";
const LOAD_TIMEOUT_MS = 15_000;
const POLL_INTERVAL_MS = 100;

// Один тег + одна Promise на весь app — множественные плееры просто await'ят её.
let factoryPromise: Promise<KinescopePlayerFactory> | null = null;

export function resolveKinescopePosterUrl(
  posterUrl?: string | null,
  baseUrl?: string,
): string | undefined {
  if (!posterUrl) return undefined;

  const base = baseUrl ?? (typeof window !== "undefined" ? window.location.href : undefined);

  if (!base) return posterUrl;

  try {
    return new URL(posterUrl, base).toString();
  } catch {
    return posterUrl;
  }
}

function loadKinescopeFactory(): Promise<KinescopePlayerFactory> {
  if (factoryPromise) return factoryPromise;
  factoryPromise = new Promise((resolve, reject) => {
    if (typeof window === "undefined") {
      reject(new Error("Kinescope iframe API requires a browser environment"));
      return;
    }
    if (window.Kinescope?.IframePlayer) {
      resolve(window.Kinescope.IframePlayer);
      return;
    }

    let script = document.getElementById(SCRIPT_ID) as HTMLScriptElement | null;
    if (!script) {
      script = document.createElement("script");
      script.id = SCRIPT_ID;
      script.async = true;
      script.src = SCRIPT_SRC;
      script.onerror = () => {
        factoryPromise = null;
        reject(new Error("Failed to load Kinescope iframe API script"));
      };
      document.body.appendChild(script);
    }

    // Цепляемся к onKinescopeIframeAPIReady — SDK вызовет его при готовности,
    // сохраняя ранее установленный handler. `settled` гасит дублирующие
    // resolve'ы (callback + poll параллельно).
    let settled = false;
    const settle = (factory: KinescopePlayerFactory) => {
      if (settled) return;
      settled = true;
      resolve(factory);
    };

    const previous = window.onKinescopeIframeAPIReady;
    window.onKinescopeIframeAPIReady = (factory) => {
      previous?.(factory);
      settle(factory);
    };

    // Fallback poll — на случай если SDK уже выставил factory до того, как мы
    // успели подписаться (race при множественных mount'ах подряд). Останавливаемся
    // как только settled=true (callback успел первым).
    const startedAt = Date.now();
    const poll = () => {
      if (settled) return;
      if (window.Kinescope?.IframePlayer) {
        settle(window.Kinescope.IframePlayer);
        return;
      }
      if (Date.now() - startedAt > LOAD_TIMEOUT_MS) {
        factoryPromise = null;
        reject(new Error(`Kinescope iframe API load timeout (${LOAD_TIMEOUT_MS}ms)`));
        return;
      }
      setTimeout(poll, POLL_INTERVAL_MS);
    };
    poll();
  });
  return factoryPromise;
}

export interface KinescopeChapterItem {
  id: string;
  startSeconds: number;
  title: string;
}

export interface KinescopeInteractivePlayerHandle {
  seekTo: (seconds: number) => Promise<void>;
}

interface KinescopeInteractivePlayerProps {
  videoId: string;
  posterUrl?: string | null;
  chapters?: KinescopeChapterItem[];
  className?: string;
  /**
   * Offset в секундах для deep-link'а — передаётся в URL как `?seek=<n>`.
   * Используется когда страница открыта с `?t=<seconds>` (результат поиска
   * по главе или клик по chapter из другого таба).
   */
  startSeconds?: number | null;
  onTimeUpdate?: (seconds: number) => void;
  onSeekChapter?: (seconds: number) => void;
}

export const KinescopeInteractivePlayer = forwardRef(function KinescopeInteractivePlayer(
  {
    videoId,
    className,
    startSeconds,
    onTimeUpdate,
    onSeekChapter,
  }: KinescopeInteractivePlayerProps,
  ref: Ref<KinescopeInteractivePlayerHandle>,
) {
  const reactId = useId();
  // factory.create() ищет элемент через getElementById; `:` в id'е работает,
  // но в querySelector требует escape — заменяем для надёжности.
  const hostId = `kinescope-host-${reactId.replace(/:/g, "-")}`;
  const playerRef = useRef<KinescopePlayerInstance | null>(null);
  const [failedVideoId, setFailedVideoId] = useState<string | null>(null);
  const [directSeek, setDirectSeek] = useState<{
    videoId: string;
    seconds: number;
    revision: number;
  } | null>(null);
  const useDirectEmbed = failedVideoId === videoId;
  const effectiveStartSeconds = directSeek?.videoId === videoId ? directSeek.seconds : startSeconds;
  const embedUrl =
    `https://kinescope.io/embed/${videoId}` +
    (typeof effectiveStartSeconds === "number" && effectiveStartSeconds > 0
      ? `?seek=${String(Math.floor(effectiveStartSeconds))}`
      : "");

  // Колбэки храним в ref'ах — иначе их identity ре-триггернет mount-effect
  // (parent передаёт новые функции каждый рендер). Sync через effect, не во
  // время рендера — React 19 / react-hooks/refs запрещает touch'ить
  // ref.current в теле компонента.
  const onTimeUpdateRef = useRef(onTimeUpdate);
  const onSeekChapterRef = useRef(onSeekChapter);
  useEffect(() => {
    onTimeUpdateRef.current = onTimeUpdate;
    onSeekChapterRef.current = onSeekChapter;
  });

  useImperativeHandle(ref, () => ({
    seekTo: async (seconds: number) => {
      const normalizedSeconds = Math.max(0, seconds);
      if (useDirectEmbed) {
        setDirectSeek((current) => ({
          videoId,
          seconds: normalizedSeconds,
          revision: current?.videoId === videoId ? current.revision + 1 : 1,
        }));
        return;
      }

      await playerRef.current?.seekTo(normalizedSeconds);
    },
  }));

  useEffect(() => {
    if (useDirectEmbed) {
      playerRef.current = null;
      return;
    }

    let cancelled = false;
    let player: KinescopePlayerInstance | null = null;
    let timeUpdateHandler: ((event: { data?: unknown }) => void) | null = null;
    let seekChapterHandler: ((event: { data?: unknown }) => void) | null = null;

    // `/embed/` префикс обязателен. Без него Kinescope грузит watch-page,
    // у которой в проде (HTTPS + Total Cookie Protection) включён cross-origin
    // блок + preview-build плеера падает с `TypeError: e is not iterable`
    // (см. #173 follow-up). На localhost HTTP браузер не применяет partitioning
    // и watch-page всё равно работает — поэтому регрессия не воспроизводилась
    // локально. Embed-page имеет правильные CORS-заголовки для iframe-контекста.
    loadKinescopeFactory()
      .then((factory) => {
        if (cancelled) return null;
        // Главы уже сохранены в самом Kinescope. Не дублируем их в playlist:
        // SDK сериализует options в длинный `_=` query, который Firefox может
        // заблокировать по CORS и оставить вместо видео чёрный iframe (#887).
        return factory.create(hostId, {
          url: embedUrl,
          size: { width: "100%", height: "100%" },
          behavior: {
            preload: "auto",
            playsInline: true,
            autoPause: true,
            localStorage: true,
          },
          ui: {
            language: "ru",
            controls: true,
            mainPlayButton: true,
            playbackRateButton: true,
          },
        });
      })
      .then((created) => {
        if (!created) return;
        // Сначала фиксируем player в outer-scope variable — иначе при cleanup'е,
        // запущенном между resolve'ом factory.create и этой строкой, ссылка
        // на player потеряется (JS single-threaded — окно микроскопическое, но
        // структура надёжнее против будущих рефакторов с awaits).
        player = created;
        playerRef.current = created;
        if (cancelled) {
          void created.destroy();
          playerRef.current = null;
          return;
        }

        timeUpdateHandler = (event) => {
          const data = event.data as { currentTime?: number } | undefined;
          if (data && typeof data.currentTime === "number") {
            onTimeUpdateRef.current?.(data.currentTime);
          }
        };
        seekChapterHandler = (event) => {
          const data = event.data as { position?: number } | undefined;
          if (data && typeof data.position === "number") {
            onSeekChapterRef.current?.(data.position);
          }
        };
        created.on(created.Events.TimeUpdate, timeUpdateHandler);
        created.on(created.Events.SeekChapter, seekChapterHandler);
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          console.error("[KinescopePlayer] init failed", error);
          setFailedVideoId(videoId);
        }
      });

    return () => {
      cancelled = true;
      if (player) {
        if (timeUpdateHandler) {
          player.off(player.Events.TimeUpdate, timeUpdateHandler);
        }
        if (seekChapterHandler) {
          player.off(player.Events.SeekChapter, seekChapterHandler);
        }
        void player.destroy();
      }
      playerRef.current = null;
    };
  }, [embedUrl, hostId, useDirectEmbed, videoId]);

  return (
    <div className={cn("overflow-hidden rounded-xl border border-border/70 bg-black", className)}>
      <div className="aspect-video">
        {/* Обычно Kinescope factory.create вставляет iframe внутрь host div.
              Если SDK/CORS ломается, React заменяет весь host на прямой iframe,
              поэтому уже вставленный SDK-узел не остаётся поверх fallback. */}
        {useDirectEmbed ? (
          <iframe
            key={`${videoId}:${directSeek?.videoId === videoId ? String(directSeek.revision) : "0"}`}
            title="Видеоурок"
            src={embedUrl}
            className="h-full w-full border-0"
            allow="autoplay; fullscreen; picture-in-picture; encrypted-media"
            allowFullScreen
            referrerPolicy="strict-origin-when-cross-origin"
          />
        ) : (
          <div id={hostId} className="h-full w-full" />
        )}
      </div>
    </div>
  );
});
