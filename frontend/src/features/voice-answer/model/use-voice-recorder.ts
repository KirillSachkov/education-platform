"use client";

import { MAX_VOICE_ANSWER_SECONDS } from "@/shared/config/trainer";
import { useEffect, useRef, useState } from "react";

/**
 * Состояние записи голоса (#585):
 * - `idle` — готов к записи;
 * - `requesting` — запрашиваем доступ к микрофону (getUserMedia);
 * - `recording` — идёт запись;
 * - `recorded` — запись готова (есть Blob + objectURL для проигрывания);
 * - `denied` — пользователь отклонил доступ к микрофону (NotAllowedError);
 * - `unsupported` — браузер не умеет getUserMedia/MediaRecorder;
 * - `error` — иной сбой (нет устройства, getUserMedia упал).
 */
export type VoiceRecorderStatus =
  | "idle"
  | "requesting"
  | "recording"
  | "recorded"
  | "denied"
  | "unsupported"
  | "error";

export interface VoiceRecorder {
  status: VoiceRecorderStatus;
  /** Готовая запись (после остановки). null пока не записано. */
  audioBlob: Blob | null;
  /** objectURL для `<audio src>` (живёт пока запись не сброшена). */
  audioUrl: string | null;
  /** Длительность записи в секундах (целое, тикает во время записи). */
  durationSec: number;
  /** Максимум длительности ответа в секундах (#663) — рекордер сам остановится на нём. */
  maxDurationSec: number;
  /** true, если последняя запись была авто-остановлена по достижению лимита длительности (#663). */
  autoStopped: boolean;
  /** Запросить микрофон и начать запись. */
  start: () => void;
  /** Остановить запись — соберёт Blob, переведёт в `recorded`. */
  stop: () => void;
  /** Сбросить к `idle`: освободит objectURL, обнулит Blob/таймер. */
  reset: () => void;
  /**
   * Live-`AnalyserNode` на потоке записи (#585) — для canvas-визуализатора уровня
   * микрофона. Не null только во время `recording`. Стабильный геттер (ref-backed).
   */
  getAnalyser: () => AnalyserNode | null;
  /** Восстановить готовую запись из внешнего blob (персист) → статус `recorded`. */
  hydrate: (blob: Blob) => void;
}

/** Поддерживается ли запись в текущем окружении (SSR-safe). */
function isRecordingSupported(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof navigator !== "undefined" &&
    !!navigator.mediaDevices &&
    typeof navigator.mediaDevices.getUserMedia === "function" &&
    typeof window.MediaRecorder !== "undefined"
  );
}

/** Предпочитаемый mime: webm/opus, с фолбэком на дефолт браузера. */
function pickMimeType(): string | undefined {
  if (typeof MediaRecorder === "undefined") return undefined;
  const candidates = ["audio/webm;codecs=opus", "audio/webm", "audio/mp4"];
  for (const type of candidates) {
    if (MediaRecorder.isTypeSupported(type)) return type;
  }
  return undefined;
}

/**
 * MediaRecorder-хук записи голосового ответа (#585). Baseline-практики: запросить
 * поток `getUserMedia({audio})` → `MediaRecorder` → собрать чанки `dataavailable`
 * в Blob. КРИТИЧНО освобождает микрофон (`track.stop()`) на стопе И размонтировании,
 * а также revoke'ает objectURL — иначе индикатор записи в браузере остаётся гореть.
 */
export function useVoiceRecorder(): VoiceRecorder {
  const [status, setStatus] = useState<VoiceRecorderStatus>("idle");
  const [audioBlob, setAudioBlob] = useState<Blob | null>(null);
  const [audioUrl, setAudioUrl] = useState<string | null>(null);
  const [durationSec, setDurationSec] = useState(0);
  // true когда запись была авто-оборвана по достижению лимита длительности ответа (#663).
  const [autoStopped, setAutoStopped] = useState(false);

  // Императивные хэндлы (не влияют на рендер) — ref ОК по правилам React 19.
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const objectUrlRef = useRef<string | null>(null);
  // Таймаут на «requesting»: если getUserMedia не resolve/reject (юзер игнорит prompt
  // или политика тихо подвесила) — не зависаем на спиннере, падаем в error (#585 QA).
  const requestTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Web Audio граф для live-«волны» уровня микрофона (визуализатор записи).
  const audioCtxRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  // Геттер анализатора — читает ref В МОМЕНТ ВЫЗОВА (из rAF-цикла canvas'а), не в рендере.
  const getAnalyser = () => analyserRef.current;

  // Рвём аудио-граф визуализатора (отдельно от MediaRecorder).
  const closeAudioGraph = () => {
    try {
      sourceRef.current?.disconnect();
    } catch {
      // уже отключён — игнорируем
    }
    sourceRef.current = null;
    analyserRef.current = null;
    const ctx = audioCtxRef.current;
    audioCtxRef.current = null;
    if (ctx && ctx.state !== "closed") void ctx.close();
  };

  // Освобождаем микрофон: рвём граф визуализатора + останавливаем все дорожки потока.
  const releaseStream = () => {
    closeAudioGraph();
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
  };

  const stopTimer = () => {
    if (timerRef.current !== null) {
      clearInterval(timerRef.current);
      timerRef.current = null;
    }
  };

  // На размонтировании — гарантированно отпускаем mic, гасим таймер, revoke URL.
  useEffect(() => {
    return () => {
      stopTimer();
      if (requestTimerRef.current !== null) clearTimeout(requestTimerRef.current);
      requestTimerRef.current = null;
      try {
        if (recorderRef.current && recorderRef.current.state !== "inactive") {
          recorderRef.current.stop();
        }
      } catch {
        // recorder уже остановлен — игнорируем
      }
      releaseStream();
      if (objectUrlRef.current) {
        URL.revokeObjectURL(objectUrlRef.current);
        objectUrlRef.current = null;
      }
    };
    // Teardown строго на размонтировании; хелперы (releaseStream/stopTimer) трогают
    // только ref'ы и стабильны по поведению — реактивных зависимостей здесь нет.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const start = () => {
    if (!isRecordingSupported()) {
      setStatus("unsupported");
      return;
    }
    if (status === "requesting" || status === "recording") return;

    // Сбрасываем прошлую запись перед новой.
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
    }
    setAudioBlob(null);
    setAudioUrl(null);
    setDurationSec(0);
    setAutoStopped(false);
    chunksRef.current = [];
    setStatus("requesting");

    // Safety-net: getUserMedia может не settle'иться (prompt проигнорен / политика
    // подвесила) — через 20с отпускаем mic и показываем ошибку вместо вечного спиннера.
    requestTimerRef.current = setTimeout(() => {
      requestTimerRef.current = null;
      releaseStream();
      setStatus("error");
    }, 20_000);

    navigator.mediaDevices
      .getUserMedia({ audio: true })
      .then((stream) => {
        // Уже отвалились по таймауту — поток больше не нужен, освобождаем и выходим.
        if (requestTimerRef.current === null) {
          stream.getTracks().forEach((track) => track.stop());
          return;
        }
        clearTimeout(requestTimerRef.current);
        requestTimerRef.current = null;
        streamRef.current = stream;

        // Live-визуализатор уровня микрофона (#585): AnalyserNode на ТОМ ЖЕ потоке.
        // Намеренно НЕ коннектим к destination — иначе пользователь слышит сам себя
        // (эхо). Best-effort: нет AudioContext → запись идёт просто без «волны».
        try {
          const AudioCtx =
            window.AudioContext ??
            (window as unknown as { webkitAudioContext?: typeof AudioContext })
              .webkitAudioContext;
          if (AudioCtx) {
            const audioCtx = new AudioCtx();
            const sourceNode = audioCtx.createMediaStreamSource(stream);
            const analyser = audioCtx.createAnalyser();
            analyser.fftSize = 1024;
            analyser.smoothingTimeConstant = 0.7;
            sourceNode.connect(analyser);
            audioCtxRef.current = audioCtx;
            sourceRef.current = sourceNode;
            analyserRef.current = analyser;
          }
        } catch {
          // визуализатор не критичен — игнорируем
        }

        const mimeType = pickMimeType();
        const recorder = mimeType
          ? new MediaRecorder(stream, { mimeType })
          : new MediaRecorder(stream);
        recorderRef.current = recorder;

        recorder.addEventListener("dataavailable", (event) => {
          if (event.data && event.data.size > 0) {
            chunksRef.current.push(event.data);
          }
        });

        recorder.addEventListener("stop", () => {
          stopTimer();
          releaseStream();
          const blob = new Blob(chunksRef.current, {
            type: recorder.mimeType || "audio/webm",
          });
          chunksRef.current = [];
          // Пустая запись (мгновенный стоп) — не считаем валидной.
          if (blob.size === 0) {
            setStatus("idle");
            return;
          }
          const url = URL.createObjectURL(blob);
          objectUrlRef.current = url;
          setAudioBlob(blob);
          setAudioUrl(url);
          setStatus("recorded");
        });

        recorder.start();
        setStatus("recording");

        const startedAt = Date.now();
        timerRef.current = setInterval(() => {
          const elapsed = Math.floor((Date.now() - startedAt) / 1000);
          setDurationSec(elapsed);
          // Достигнут лимит длительности ответа (#663) — авто-стоп. recorder.stop() поднимет
          // 'stop'-обработчик (соберёт Blob + погасит таймер); флаг включит подсказку в UI.
          if (elapsed >= MAX_VOICE_ANSWER_SECONDS) {
            setAutoStopped(true);
            if (recorderRef.current && recorderRef.current.state !== "inactive") {
              recorderRef.current.stop();
            } else {
              stopTimer();
            }
          }
        }, 250);
      })
      .catch((err: unknown) => {
        if (requestTimerRef.current === null) return; // уже обработали таймаутом
        clearTimeout(requestTimerRef.current);
        requestTimerRef.current = null;
        releaseStream();
        const name = err instanceof DOMException ? err.name : "";
        // Отказ в доступе → denied, прочее (нет устройства и т.п.) → error.
        setStatus(
          name === "NotAllowedError" || name === "SecurityError" ? "denied" : "error",
        );
      });
  };

  const stop = () => {
    if (recorderRef.current && recorderRef.current.state !== "inactive") {
      // Финальный чанк прилетит синхронно, затем сработает 'stop'-обработчик.
      recorderRef.current.stop();
    } else {
      stopTimer();
      releaseStream();
    }
  };

  const reset = () => {
    stopTimer();
    if (requestTimerRef.current !== null) clearTimeout(requestTimerRef.current);
    requestTimerRef.current = null;
    if (recorderRef.current && recorderRef.current.state !== "inactive") {
      try {
        recorderRef.current.stop();
      } catch {
        // уже остановлен
      }
    }
    recorderRef.current = null;
    releaseStream();
    chunksRef.current = [];
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
    }
    setAudioBlob(null);
    setAudioUrl(null);
    setDurationSec(0);
    setAutoStopped(false);
    setStatus(isRecordingSupported() ? "idle" : "unsupported");
  };

  // Восстановить готовую запись из персиста (IndexedDB): не пишем ничего сами, просто
  // материализуем objectURL и переводим в `recorded`. Длительность из персиста не знаем
  // (плеер возьмёт точную из decodeAudioData). Пустой/повторный blob — игнор.
  const hydrate = (blob: Blob) => {
    if (blob.size === 0 || audioBlob === blob) return;
    if (objectUrlRef.current) {
      URL.revokeObjectURL(objectUrlRef.current);
      objectUrlRef.current = null;
    }
    const url = URL.createObjectURL(blob);
    objectUrlRef.current = url;
    chunksRef.current = [];
    setAudioBlob(blob);
    setAudioUrl(url);
    setStatus("recorded");
  };

  return {
    status,
    audioBlob,
    audioUrl,
    durationSec,
    maxDurationSec: MAX_VOICE_ANSWER_SECONDS,
    autoStopped,
    start,
    stop,
    reset,
    getAnalyser,
    hydrate,
  };
}
