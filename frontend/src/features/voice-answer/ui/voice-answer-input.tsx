"use client";

import { formatTrainerClock, MAX_VOICE_ANSWER_LABEL } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useEffect, useRef, useState } from "react";
import { useVoiceRecorder } from "../model/use-voice-recorder";
import {
  deleteVoiceRecording,
  loadVoiceRecording,
  saveVoiceRecording,
} from "../model/voice-recording-store";

/** Активный способ ответа на открытый вопрос: запись голоса или ручной текст. */
export type VoiceAnswerMode = "voice" | "text";

interface VoiceAnswerInputProps {
  /** Текст ответа (controlled) — для текстового режима. */
  value: string;
  onChange: (next: string) => void;
  /**
   * Записанный голосовой Blob (#585): сервер сам транскрибирует и грейдит на
   * «Ответить». null — записи нет (ещё не записали / перезаписываем). Раннер по
   * нему решает, можно ли отправлять голосом.
   */
  onVoiceRecorded: (blob: Blob | null) => void;
  /** Текущий способ ответа — раннер по нему выбирает, что слать на «Ответить». */
  onModeChange: (mode: VoiceAnswerMode) => void;
  /**
   * Ключ персиста записи (#585) — `session:item`. Если задан, запись сохраняется в
   * IndexedDB и переживает навигацию между вопросами / смену вкладки / перезагрузку:
   * при монтировании панель подгружает сохранённый blob. null/отсутствует — без персиста.
   */
  persistKey?: string;
  disabled?: boolean;
}

/**
 * Ввод ответа на открытый вопрос голосом или текстом (#585). Сервер сам
 * транскрибирует и грейдит — студент НЕ распознаёт/не правит текст. Голосовой
 * режим: Запись → Стоп → проигрывание `<audio>` + «Перезаписать»; на «Ответить»
 * (в раннере) уходит сам записанный аудио-Blob. Текстовый режим — по тумблеру
 * «Текстом», обычная textarea. Грейсфул: отказ микрофона / неподдержка / сбой →
 * подсказка + всегда можно набрать текст. Раннер получает активный режим
 * (`onModeChange`) и записанный Blob (`onVoiceRecorded`), чтобы знать, что слать.
 */
export function VoiceAnswerInput({
  value,
  onChange,
  onVoiceRecorded,
  onModeChange,
  persistKey,
  disabled = false,
}: VoiceAnswerInputProps) {
  const recorder = useVoiceRecorder();
  // Тумблер: голос vs ручной ввод. Если запись не поддерживается — сразу текст.
  const [manualMode, setManualMode] = useState(false);

  const unsupported = recorder.status === "unsupported";
  const hasRecording = recorder.audioBlob !== null; // есть завершённая запись
  const isRecordingNow = recorder.status === "recording" || recorder.status === "requesting";
  // Голос и текст взаимоисключаемы (#585): пока есть запись — текст недоступен (удали запись).
  const showText = (manualMode && !hasRecording) || unsupported;
  const activeMode: VoiceAnswerMode = showText ? "text" : "voice";
  // В тексте голос неактуален → null. Раннер шлёт этот Blob на «Ответить».
  const reportedBlob = showText ? null : recorder.audioBlob;

  // Персист: при монтировании / смене ключа подгружаем сохранённую запись (latest-ref
  // на hydrate — он пересоздаётся каждый рендер, а грузить надо один раз на ключ).
  const hydrateRef = useRef(recorder.hydrate);
  useEffect(() => {
    hydrateRef.current = recorder.hydrate;
  });
  useEffect(() => {
    if (!persistKey) return;
    let cancelled = false;
    void loadVoiceRecording(persistKey).then((blob) => {
      if (!cancelled && blob) hydrateRef.current(blob);
    });
    return () => {
      cancelled = true;
    };
  }, [persistKey]);

  // Сохраняем готовую запись под ключом (идемпотентно — повторный put безвреден).
  useEffect(() => {
    if (persistKey && recorder.audioBlob) void saveVoiceRecording(persistKey, recorder.audioBlob);
  }, [persistKey, recorder.audioBlob]);

  // XOR: появилась запись → стираем набранный текст (нельзя держать оба ответа).
  useEffect(() => {
    if (recorder.audioBlob && value) onChange("");
  }, [recorder.audioBlob, value, onChange]);

  // Поднимаем наверх ТОЛЬКО при реальном изменении (ref на последнее значение) —
  // так раннер не зациклится, даже если передаёт inline-колбэки. Запись Blob/mode
  // в parent делаем эффектом (не присвоением в рендере) по правилам React 19.
  const lastBlobRef = useRef<Blob | null>(null);
  useEffect(() => {
    if (lastBlobRef.current !== reportedBlob) {
      lastBlobRef.current = reportedBlob;
      onVoiceRecorded(reportedBlob);
    }
  }, [reportedBlob, onVoiceRecorded]);

  const lastModeRef = useRef<VoiceAnswerMode | null>(null);
  useEffect(() => {
    if (lastModeRef.current !== activeMode) {
      lastModeRef.current = activeMode;
      onModeChange(activeMode);
    }
  }, [activeMode, onModeChange]);

  const selectVoice = () => setManualMode(false);
  // На текст переключаемся только когда записи нет (иначе кнопка disabled) — запись не теряем.
  const selectText = () => setManualMode(true);

  // Удалить запись: освобождает текстовый режим + стирает из персиста.
  const deleteRecording = () => {
    recorder.reset();
    if (persistKey) void deleteVoiceRecording(persistKey);
  };

  return (
    <div className="grid gap-3">
      {/* Равноправный выбор способа ответа: голос и текст — в один ряд, наравне. */}
      {!unsupported && (
        <div className="grid grid-cols-2 gap-2">
          <ModeButton
            active={!showText}
            icon={Icons.mic}
            disabled={disabled || recorder.status === "recording"}
            onClick={selectVoice}
          >
            Голосом
          </ModeButton>
          <ModeButton
            active={showText}
            icon={Icons.editAlt}
            disabled={disabled || isRecordingNow || hasRecording}
            onClick={selectText}
          >
            Текстом
          </ModeButton>
        </div>
      )}

      {!showText && (
        <VoicePanel recorder={recorder} disabled={disabled} onDelete={deleteRecording} />
      )}

      {/* Текстовый ответ — обычная textarea (только в ручном режиме). */}
      {showText && (
        <Textarea
          value={value}
          onChange={(event) => onChange(event.target.value)}
          disabled={disabled}
          placeholder="Ответь тезисами — потом сверишься с эталоном…"
          className="min-h-28 bg-card/50"
        />
      )}

      <p className="text-xs text-muted-foreground">
        {unsupported
          ? "Браузер не поддерживает запись — набери ответ текстом."
          : hasRecording
            ? "Запись готова. Чтобы ответить текстом — удали запись."
            : showText
              ? "Набери ответ — проверка будет после отправки."
              : "Запиши ответ голосом — мы сами его расшифруем и проверим."}
      </p>
    </div>
  );
}

/** Равноправная кнопка выбора способа ответа (голос/текст) — обе одного веса. */
function ModeButton({
  active,
  icon: Icon,
  disabled,
  onClick,
  children,
}: {
  active: boolean;
  icon: React.ComponentType<{ className?: string }>;
  disabled?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      className={cn(
        "flex min-h-[44px] items-center justify-center gap-2 rounded-lg border px-4 text-sm font-medium transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
        "disabled:cursor-not-allowed disabled:opacity-50",
        active
          ? "border-primary bg-primary/10 text-primary"
          : "border-border/60 bg-card/50 text-foreground/80 hover:bg-accent/40",
      )}
    >
      <Icon className="size-4" />
      {children}
    </button>
  );
}

/** Панель записи/проигрывания голоса. */
function VoicePanel({
  recorder,
  disabled,
  onDelete,
}: {
  recorder: ReturnType<typeof useVoiceRecorder>;
  disabled: boolean;
  onDelete: () => void;
}) {
  const { status, audioUrl, audioBlob, durationSec, maxDurationSec, autoStopped } = recorder;

  // Отказ в доступе — подсказка + кнопка «попробовать снова».
  if (status === "denied") {
    return (
      <Hint tone="warn">
        <Icons.micOff className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <span>
          Доступ к микрофону запрещён. Разреши его в настройках браузера или набери ответ
          текстом ниже.
        </span>
      </Hint>
    );
  }

  // Иной сбой записи (нет устройства / getUserMedia упал).
  if (status === "error") {
    return (
      <div className="grid gap-2">
        <Hint tone="warn">
          <Icons.warning className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>Не удалось включить запись. Попробуй снова или набери текст ниже.</span>
        </Hint>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="w-fit"
          disabled={disabled}
          onClick={recorder.start}
        >
          <Icons.mic className="size-4" />
          Попробовать снова
        </Button>
      </div>
    );
  }

  // Готовая запись — компактный плеер (Web Audio) + перезаписать. key={audioUrl}
  // ремаунтит панель на каждую новую запись → пере-декодирует blob, сбрасывает playing/failed.
  if (status === "recorded" && audioUrl && audioBlob) {
    return (
      <div className="grid gap-2">
        {autoStopped && (
          <Hint tone="warn">
            <Icons.warning className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
            <span>Запись остановлена на лимите — максимум {MAX_VOICE_ANSWER_LABEL} на ответ.</span>
          </Hint>
        )}
        <RecordedPanel
          key={audioUrl}
          audioBlob={audioBlob}
          durationSec={durationSec}
          disabled={disabled}
          onRerecord={recorder.start}
          onDelete={onDelete}
        />
      </div>
    );
  }

  // Идёт запись — таймер + пульс + live-«волна» уровня микрофона + «Стоп».
  if (status === "recording") {
    return (
      <div className="grid gap-3 rounded-lg border border-destructive/40 bg-destructive/5 p-3">
        <div className="flex items-center gap-3">
          <span className="relative flex size-3 shrink-0">
            <span className="absolute inline-flex size-full animate-ping rounded-full bg-destructive/60" />
            <span className="relative inline-flex size-3 rounded-full bg-destructive" />
          </span>
          <span className="font-mono text-sm tabular-nums text-foreground">
            {formatTrainerClock(durationSec)}
            <span className="text-muted-foreground"> / {formatTrainerClock(maxDurationSec)}</span>
          </span>
          <span className="text-sm text-muted-foreground">Идёт запись…</span>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            className="ml-auto min-h-[44px]"
            onClick={recorder.stop}
          >
            <Icons.stop className="size-4 fill-current" />
            Стоп
          </Button>
        </div>
        <LiveWaveform getAnalyser={recorder.getAnalyser} />
      </div>
    );
  }

  // idle / requesting — кнопка «Записать» + явная подпись лимита длительности (#663).
  return (
    <div className="grid w-fit gap-1.5">
      <Button
        type="button"
        variant="outline"
        className="min-h-[44px] w-fit"
        disabled={disabled || status === "requesting"}
        onClick={recorder.start}
      >
        {status === "requesting" ? (
          <Icons.loading className="size-4 animate-spin" />
        ) : (
          <Icons.mic className="size-4" />
        )}
        {status === "requesting" ? "Запрашиваем доступ…" : "Записать голосом"}
      </Button>
      <p className="text-xs text-muted-foreground">До {MAX_VOICE_ANSWER_LABEL} на ответ.</p>
    </div>
  );
}

/**
 * Готовая запись: компактный плеер «Прослушать» + «Перезаписать». Транскрипция/грейдинг —
 * серверные: студент текст не видит и не правит.
 *
 * Воспроизведение (#585): нативный `<audio>` ненадёжно играл webm/opus-blob MediaRecorder
 * (`duration=Infinity` → `play()` мгновенно «заканчивался», симптом «прослушивание не
 * работает»). Поэтому играем через Web Audio: декодируем blob в `AudioBuffer`
 * (`decodeAudioData` — браузер умеет декодировать то, что сам записал) и проигрываем
 * одноразовым `AudioBufferSourceNode`. Длительность показываем из таймера записи; декод
 * не удался → graceful «не удалось воспроизвести» (на проверку запись всё равно уйдёт).
 * Панель ремаунтится по `key={audioUrl}` на каждую новую запись → пере-декод + сброс.
 */
function RecordedPanel({
  audioBlob,
  durationSec,
  disabled,
  onRerecord,
  onDelete,
}: {
  audioBlob: Blob;
  durationSec: number;
  disabled: boolean;
  onRerecord: () => void;
  onDelete: () => void;
}) {
  const [playing, setPlaying] = useState(false);
  const [failed, setFailed] = useState(false);
  // Точная длительность из декода (для персиста таймер записи неизвестен → берём отсюда).
  const [decodedDuration, setDecodedDuration] = useState<number | null>(null);

  const ctxRef = useRef<AudioContext | null>(null);
  const bufferRef = useRef<AudioBuffer | null>(null);
  const sourceRef = useRef<AudioBufferSourceNode | null>(null);

  const stopSource = () => {
    const src = sourceRef.current;
    if (!src) return;
    src.onended = null;
    try {
      src.stop();
    } catch {
      // уже остановлен
    }
    sourceRef.current = null;
  };

  // Декодируем blob один раз (панель ремаунтится по key на каждую новую запись).
  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const arrayBuffer = await audioBlob.arrayBuffer();
        const AudioCtx =
          window.AudioContext ??
          (window as unknown as { webkitAudioContext?: typeof AudioContext })
            .webkitAudioContext;
        if (!AudioCtx) {
          if (!cancelled) setFailed(true);
          return;
        }
        const ctx = new AudioCtx();
        ctxRef.current = ctx;
        const decoded = await ctx.decodeAudioData(arrayBuffer);
        if (!cancelled) {
          bufferRef.current = decoded;
          setDecodedDuration(Math.round(decoded.duration));
        }
      } catch {
        if (!cancelled) setFailed(true);
      }
    })();
    return () => {
      cancelled = true;
      stopSource();
      bufferRef.current = null;
      const ctx = ctxRef.current;
      ctxRef.current = null;
      if (ctx && ctx.state !== "closed") void ctx.close();
    };
  }, [audioBlob]);

  const togglePlay = () => {
    if (playing) {
      stopSource(); // source одноразовый — пересоздаём на следующем play
      setPlaying(false);
      return;
    }
    const ctx = ctxRef.current;
    const buffer = bufferRef.current;
    if (!ctx || !buffer) {
      setFailed(true);
      return;
    }
    void ctx.resume(); // мог быть suspended (автоплей-политика)
    const source = ctx.createBufferSource();
    source.buffer = buffer;
    source.connect(ctx.destination);
    source.onended = () => {
      sourceRef.current = null;
      setPlaying(false);
    };
    source.start();
    sourceRef.current = source;
    setPlaying(true);
  };

  return (
    <div className="grid gap-3 rounded-lg border border-border/60 bg-card/50 p-3">
      <div className="flex items-center gap-3">
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="min-h-[40px] w-fit"
          disabled={disabled || failed}
          onClick={togglePlay}
        >
          {playing ? (
            <Icons.stop className="size-4 fill-current" />
          ) : (
            <Icons.play className="size-4 fill-current" />
          )}
          {playing ? "Стоп" : "Прослушать"}
        </Button>
        <span className="font-mono text-sm tabular-nums text-muted-foreground">
          {formatTrainerClock(decodedDuration ?? durationSec)}
        </span>
      </div>
      {failed && (
        <p className="text-xs text-muted-foreground">
          Не удалось воспроизвести в этом браузере — запись всё равно уйдёт на проверку.
        </p>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={onRerecord}
        >
          <Icons.mic className="size-4" />
          Перезаписать
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="text-muted-foreground"
          disabled={disabled}
          onClick={onDelete}
        >
          <Icons.delete className="size-4" />
          Удалить
        </Button>
        <span className="text-xs text-muted-foreground">
          Запись готова — нажми «Ответить».
        </span>
      </div>
    </div>
  );
}

/**
 * Живая «волна» уровня микрофона во время записи (#585) — функциональный индикатор
 * «слышно ли тебя». Амплитудные бары (time-domain из `AnalyserNode` рекордера) рисуются
 * на canvas через requestAnimationFrame: тишина → тонкая линия по центру, голос → пляшущие
 * бары по всей ширине. Рисуем напрямую в canvas (без setState на кадр), цвет — currentColor.
 */
function LiveWaveform({ getAnalyser }: { getAnalyser: () => AnalyserNode | null }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  // Держим getAnalyser в ref'е — rAF-эффект ниже монтируется ОДИН раз ([] deps),
  // а панель записи ре-рендерится каждые 250мс (тикает таймер). Без этого эффект бы
  // перезапускался и дёргал canvas/rAF на каждом тике.
  const getAnalyserRef = useRef(getAnalyser);
  useEffect(() => {
    getAnalyserRef.current = getAnalyser;
  }, [getAnalyser]);

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;

    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const syncSize = () => {
      canvas.width = Math.max(1, Math.floor(canvas.clientWidth * dpr));
      canvas.height = Math.max(1, Math.floor(canvas.clientHeight * dpr));
    };
    syncSize();

    const BAR_COUNT = 48;
    let samples = new Uint8Array(0);
    const heights = new Float32Array(BAR_COUNT); // сглаженные высоты 0..1, переносятся между кадрами
    let raf = 0;

    const draw = () => {
      raf = requestAnimationFrame(draw);
      const w = canvas.width;
      const h = canvas.height;
      ctx.clearRect(0, 0, w, h);
      ctx.fillStyle = getComputedStyle(canvas).color;

      // Time-domain (амплитуда по времени), НЕ спектр: каждый бар = громкость на своём
      // отрезке времени → заполняется вся ширина равномерно (спектр голоса смещён в
      // низы → правая часть была бы мёртвой).
      const analyser = getAnalyserRef.current();
      if (analyser && samples.length !== analyser.fftSize) {
        samples = new Uint8Array(analyser.fftSize);
      }
      if (analyser) analyser.getByteTimeDomainData(samples);

      const gap = dpr * 2;
      const barW = (w - gap * (BAR_COUNT - 1)) / BAR_COUNT;
      const slice = analyser ? Math.max(1, Math.floor(samples.length / BAR_COUNT)) : 0;

      for (let i = 0; i < BAR_COUNT; i++) {
        // Целевая амплитуда отрезка = RMS (мягче, чем пик), усилена под уровень голоса.
        let target = 0;
        if (analyser) {
          let sumSq = 0;
          for (let j = 0; j < slice; j++) {
            const dev = samples[i * slice + j] - 128;
            sumSq += dev * dev;
          }
          target = Math.min(1, (Math.sqrt(sumSq / slice) / 128) * 2.8);
        }
        // Темпоральное сглаживание (как VU-метр): быстрый подъём, мягкий спад — не дёргается.
        const ease = target > heights[i] ? 0.4 : 0.12;
        heights[i] += (target - heights[i]) * ease;

        const barH = Math.max(dpr * 2, heights[i] * h);
        const radius = Math.min(barW / 2, barH / 2);
        ctx.beginPath();
        ctx.roundRect(i * (barW + gap), (h - barH) / 2, barW, barH, radius);
        ctx.fill();
      }
    };
    draw();

    window.addEventListener("resize", syncSize);
    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", syncSize);
    };
  }, []);

  return <canvas ref={canvasRef} aria-hidden="true" className="h-12 w-full text-destructive/80" />;
}

/** Компактная подсказка-плашка (предупреждение). */
function Hint({ tone, children }: { tone: "warn"; children: React.ReactNode }) {
  return (
    <p
      className={cn(
        "flex items-start gap-2 rounded-lg border p-3 text-sm",
        tone === "warn"
          ? "border-amber-500/40 bg-amber-500/5 text-foreground/80"
          : "border-border/60 bg-card/50 text-muted-foreground",
      )}
    >
      {children}
    </p>
  );
}
