"use client";

import { useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import {
  Code,
  Monitor,
  Video,
  MessageSquare,
  GitBranch,
  Send,
  CheckCircle2,
  Loader2,
  ExternalLink,
  Reply,
  Heart,
  ListChecks,
  XCircle,
} from "lucide-react";
import { KinescopePlayer } from "@/shared/ui/components/kinescope-player";
import { useReducedMotion } from "../hooks/use-reduced-motion";

// ---------------------------------------------------------------------------
// Shared animation helpers
// ---------------------------------------------------------------------------

const popIn = (i: number) => ({
  hidden: { opacity: 0, scale: 0.6 },
  visible: {
    opacity: 1,
    scale: 1,
    transition: { delay: i * 0.1, type: "spring" as const, stiffness: 400, damping: 20 },
  },
});

// ---------------------------------------------------------------------------
// MockupVideoPlayer
// ---------------------------------------------------------------------------

function MockupVideoPlayer() {
  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center gap-2 text-xs text-white/40">
        <Video className="h-3.5 w-3.5 text-[#6BADA5]" />
        <span>Пример урока</span>
      </div>
      <KinescopePlayer videoId="kVV5eJGd5mSsG63QkcMsQq" className="rounded-lg" />
    </div>
  );
}

// ---------------------------------------------------------------------------
// MockupTaskAssignment
// ---------------------------------------------------------------------------

type SubmitStatus = "idle" | "submitting" | "reviewing" | "completed";

function MockupTaskAssignment() {
  const [status, setStatus] = useState<SubmitStatus>("idle");

  const handleSubmit = () => {
    if (status !== "idle") return;
    setStatus("submitting");
    setTimeout(() => setStatus("reviewing"), 800);
    setTimeout(() => setStatus("completed"), 2200);
  };

  const statusConfig = {
    idle: { label: "В работе", bg: "rgba(59,130,246,0.1)", color: "rgb(96,165,250)" },
    submitting: { label: "Отправка...", bg: "rgba(234,179,8,0.1)", color: "rgb(250,204,21)" },
    reviewing: { label: "На ревью", bg: "rgba(234,179,8,0.1)", color: "rgb(250,204,21)" },
    completed: { label: "Принято ✓", bg: "rgba(34,197,94,0.1)", color: "rgb(74,222,128)" },
  };

  const cfg = statusConfig[status];

  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-xs text-white/40">
          <Code className="h-3.5 w-3.5 text-[#6BADA5]" />
          <span>Задание DS-9</span>
        </div>
        <motion.span
          className="rounded-full px-2 py-0.5 text-xs"
          animate={{ backgroundColor: cfg.bg, color: cfg.color }}
          transition={{ duration: 0.3 }}
        >
          {cfg.label}
        </motion.span>
      </div>
      <p className="text-sm font-medium">CRUD-команды для Directory Service</p>

      {/* PR link field */}
      <div className="flex items-center gap-2 rounded-lg bg-[#0A0A0B] px-3 py-2">
        <GitBranch className="h-3.5 w-3.5 shrink-0 text-white/20" />
        <span className="flex-1 truncate font-mono text-xs text-white/40">
          github.com/student/feature/ds-9
        </span>
        <ExternalLink className="h-3 w-3 shrink-0 text-white/20" />
      </div>

      {/* Submit button */}
      <button
        type="button"
        onClick={handleSubmit}
        disabled={status !== "idle"}
        className="flex w-full items-center justify-center gap-2 rounded-lg bg-[#6BADA5] px-4 py-2 text-xs font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9] disabled:opacity-50"
      >
        {status === "idle" && (
          <>
            <Send className="h-3.5 w-3.5" /> Отправить на ревью
          </>
        )}
        {status === "submitting" && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
        {status === "reviewing" && (
          <>
            <Loader2 className="h-3.5 w-3.5 animate-spin" /> Проверяется...
          </>
        )}
        {status === "completed" && (
          <>
            <CheckCircle2 className="h-3.5 w-3.5" /> Задание принято!
          </>
        )}
      </button>

      {/* AI-review feedback */}
      <AnimatePresence>
        {status === "completed" && (
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -4 }}
            className="rounded-lg border border-[#6BADA5]/20 bg-[#6BADA5]/5 p-3"
          >
            <div className="mb-1.5 flex items-center gap-2">
              <div className="flex h-5 w-5 items-center justify-center rounded-full bg-[#6BADA5]/20 text-[10px] font-medium text-[#6BADA5]">
                AI
              </div>
              <span className="text-xs font-semibold text-[#6BADA5]">AI-ревью</span>
            </div>
            <p className="text-xs text-white/60">
              Отличное выполнение! Чистый Result паттерн, правильный soft delete. Так выглядит
              AI-ревью каждого PR с заданием — записывайся, чтобы получить такой же фидбек на свой
              код.
            </p>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ---------------------------------------------------------------------------
// MockupCodeReview
// ---------------------------------------------------------------------------

function MockupCodeReview({ isActive }: { isActive: boolean }) {
  const reduced = useReducedMotion();
  const animate = isActive && !reduced;

  const diffLines = [
    { cls: "text-red-400/60 bg-red-400/[0.03]", text: "- public void Delete(Guid id)" },
    { cls: "text-green-400/60 bg-green-400/[0.03]", text: "+ public Result Delete(Guid id)" },
    { cls: "text-green-400/60 bg-green-400/[0.03]", text: "+ {" },
    { cls: "text-green-400/60 bg-green-400/[0.03]", text: "+   if (!_repo.Exists(id))" },
    { cls: "text-green-400/60 bg-green-400/[0.03]", text: "+     return Errors.NotFound;" },
    { cls: "text-green-400/60 bg-green-400/[0.03]", text: "+ }" },
  ];

  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center gap-2 text-xs text-white/40">
        <GitBranch className="h-3.5 w-3.5 text-[#6BADA5]" />
        <span>Pull Request #42</span>
        <span className="rounded-full bg-green-500/10 px-2 py-0.5 text-xs text-green-400">
          +186 −23
        </span>
      </div>
      <div className="rounded-lg bg-[#0A0A0B] p-3 font-mono text-xs">
        <div className="mb-2 border-b border-white/[0.04] pb-2 text-white/25">
          LocationService.cs
        </div>
        <div className="space-y-0.5">
          {diffLines.map((l, i) => (
            <motion.p
              key={i}
              className={`rounded px-1.5 py-0.5 ${l.cls}`}
              initial={reduced ? undefined : { opacity: 0, x: -12 }}
              animate={animate ? { opacity: 1, x: 0 } : undefined}
              transition={{ delay: 0.3 + i * 0.25, duration: 0.35 }}
            >
              {l.text}
            </motion.p>
          ))}
        </div>
      </div>
      <motion.div
        className="rounded-lg border border-[#6BADA5]/20 bg-[#6BADA5]/5 p-3"
        initial={reduced ? undefined : { opacity: 0, y: 8 }}
        animate={animate ? { opacity: 1, y: 0 } : undefined}
        transition={{ delay: 1.5, duration: 0.4 }}
      >
        <div className="mb-1 flex items-center gap-2">
          <div className="flex h-5 w-5 items-center justify-center rounded-full bg-[#6BADA5]/20 text-[10px] font-medium text-[#6BADA5]">
            AI
          </div>
          <span className="text-xs font-medium text-[#6BADA5]">AI-ревью</span>
          <span className="text-[10px] text-white/20">2 мин назад</span>
        </div>
        <p className="text-xs text-white/60">
          Отлично, что перешёл на Result. Добавь ещё TransactionScope для атомарности.
        </p>
      </motion.div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// MockupCommunity
// ---------------------------------------------------------------------------

function MockupCommunity({ isActive }: { isActive: boolean }) {
  const reduced = useReducedMotion();
  const animate = isActive && !reduced;

  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-xs text-white/40">
          <MessageSquare className="h-3.5 w-3.5 text-[#6BADA5]" />
          <span>Обсуждение • Модуль 8</span>
        </div>
        <span className="text-[10px] text-white/20">3 комментария</span>
      </div>

      <div className="space-y-0">
        {/* Root comment */}
        <motion.div
          className="rounded-lg bg-[#0A0A0B] p-3"
          initial={reduced ? undefined : { opacity: 0, y: 8 }}
          animate={animate ? { opacity: 1, y: 0 } : undefined}
          transition={{ delay: 0.2 }}
        >
          <div className="flex items-center gap-2">
            <div className="flex h-6 w-6 items-center justify-center rounded-full bg-blue-500/20 text-[10px] font-medium text-blue-400">
              АМ
            </div>
            <span className="text-xs font-medium">Алексей</span>
            <span className="text-[10px] text-white/20">вчера</span>
          </div>
          <p className="mt-1.5 text-xs text-white/50">
            Разобрался с Outbox паттерном — сообщения теперь атомарно сохраняются с доменными
            событиями
          </p>
          <div className="mt-2 flex items-center gap-3 text-[10px] text-white/25">
            <button
              type="button"
              className="flex items-center gap-1 transition-colors hover:text-white/40"
            >
              <Heart className="h-3 w-3" /> 4
            </button>
            <button
              type="button"
              className="flex items-center gap-1 transition-colors hover:text-white/40"
            >
              <Reply className="h-3 w-3" /> Ответить
            </button>
          </div>
        </motion.div>

        {/* Reply (threaded) */}
        <motion.div
          className="ml-4 border-l border-white/[0.06] pl-3 pt-2"
          initial={reduced ? undefined : { opacity: 0, y: 8 }}
          animate={animate ? { opacity: 1, y: 0 } : undefined}
          transition={{ delay: 0.5 }}
        >
          <div className="rounded-lg bg-[#0A0A0B] p-3">
            <div className="flex items-center gap-2">
              <div className="flex h-6 w-6 items-center justify-center rounded-full bg-[#6BADA5]/20 text-[10px] font-medium text-[#6BADA5]">
                КС
              </div>
              <span className="text-xs font-medium">Кирилл</span>
              <span className="rounded bg-[#6BADA5]/10 px-1 py-px text-[9px] text-[#6BADA5]">
                автор
              </span>
              <span className="text-[10px] text-white/20">вчера</span>
            </div>
            <p className="mt-1.5 text-xs text-white/50">
              Отлично! Скинь ссылку на PR, посмотрю реализацию.
            </p>
          </div>

          {/* Nested reply */}
          <motion.div
            className="ml-3 border-l border-white/[0.04] pl-3 pt-2"
            initial={reduced ? undefined : { opacity: 0, y: 6 }}
            animate={animate ? { opacity: 1, y: 0 } : undefined}
            transition={{ delay: 0.8 }}
          >
            <div className="rounded-lg bg-[#0A0A0B] p-3">
              <div className="flex items-center gap-2">
                <div className="flex h-6 w-6 items-center justify-center rounded-full bg-blue-500/20 text-[10px] font-medium text-blue-400">
                  АМ
                </div>
                <span className="text-xs font-medium">Алексей</span>
                <span className="text-[10px] text-white/20">сегодня</span>
              </div>
              <p className="mt-1.5 text-xs text-white/50">Конечно, подготовлю слайды</p>
            </div>
          </motion.div>
        </motion.div>
      </div>

      {/* Decorative input */}
      <div className="flex items-center gap-2 rounded-lg bg-[#0A0A0B] px-3 py-2">
        <span className="flex-1 text-xs text-white/20">Написать комментарий...</span>
        <Send className="h-3.5 w-3.5 text-white/15" />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// MockupPlatform
// ---------------------------------------------------------------------------

function MockupPlatform({ isActive }: { isActive: boolean }) {
  const reduced = useReducedMotion();
  const animate = isActive && !reduced;

  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center gap-2 text-xs text-white/40">
        <Monitor className="h-3.5 w-3.5 text-[#6BADA5]" />
        <span>Прогресс обучения</span>
      </div>

      {/* Track progress */}
      <div className="space-y-2">
        <div className="flex items-center justify-between text-sm">
          <span className="font-medium">Трек 1: Фундамент</span>
          <span className="text-[#6BADA5]">67%</span>
        </div>
        <div className="h-2 rounded-full bg-white/[0.06]">
          <motion.div
            className="h-2 rounded-full bg-gradient-to-r from-[#6BADA5] to-[#5CEAC9]"
            initial={{ width: "0%" }}
            animate={animate ? { width: "67%" } : { width: "67%" }}
            transition={animate ? { duration: 1.5, ease: "easeOut", delay: 0.2 } : { duration: 0 }}
          />
        </div>
      </div>

      {/* Stats grid */}
      <div className="grid grid-cols-3 gap-2 text-center">
        {[
          { val: "2", label: "модуля" },
          { val: "12", label: "Уроков" },
          { val: "38", label: "заданий" },
        ].map((s, i) => (
          <motion.div
            key={s.label}
            className="rounded-lg bg-[#0A0A0B] p-2"
            initial={reduced ? undefined : popIn(i).hidden}
            animate={animate ? popIn(i).visible : undefined}
          >
            <p className="text-lg font-bold text-[#6BADA5]">{s.val}</p>
            <p className="text-[10px] text-white/30">{s.label}</p>
          </motion.div>
        ))}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// MockupQuiz — interactive «тест на понимание» after a topic
// ---------------------------------------------------------------------------

function MockupQuiz() {
  const [selected, setSelected] = useState<number | null>(null);
  const correctIndex = 1;
  const answered = selected !== null;

  const options = [
    "Сообщения уходят в брокер мгновенно",
    "Событие и доменный стейт сохраняются атомарно",
    "Exactly-once доставку гарантирует сам RabbitMQ",
    "Очередь обрабатывается в несколько потоков",
  ];

  return (
    <div className="space-y-3 p-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2 text-xs text-white/40">
          <ListChecks className="h-3.5 w-3.5 text-[#6BADA5]" />
          <span>Тест · Модуль 8</span>
        </div>
        <span className="text-[10px] text-white/20">Вопрос 3 из 5</span>
      </div>

      <p className="text-sm font-medium">Что гарантирует Outbox-паттерн?</p>

      <div className="space-y-1.5">
        {options.map((opt, i) => {
          const isCorrect = i === correctIndex;
          const isWrongPick = answered && selected === i && !isCorrect;
          const cls = !answered
            ? "border-white/[0.06] bg-[#0A0A0B] text-white/60 hover:border-[#6BADA5]/30"
            : isCorrect
              ? "border-[#6BADA5]/40 bg-[#6BADA5]/10 text-[#6BADA5]"
              : isWrongPick
                ? "border-red-500/30 bg-red-500/5 text-red-400/80"
                : "border-white/[0.04] bg-[#0A0A0B] text-white/25";
          return (
            <button
              type="button"
              key={i}
              onClick={() => !answered && setSelected(i)}
              disabled={answered}
              className={`flex w-full items-center gap-2 rounded-lg border px-3 py-2 text-left text-xs transition-colors ${cls}`}
            >
              <span className="flex-1">{opt}</span>
              {answered && isCorrect && <CheckCircle2 className="h-3.5 w-3.5 shrink-0" />}
              {isWrongPick && <XCircle className="h-3.5 w-3.5 shrink-0" />}
            </button>
          );
        })}
      </div>

      <AnimatePresence>
        {answered && (
          <motion.div
            initial={{ opacity: 0, y: 8 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -4 }}
            className="rounded-lg border border-[#6BADA5]/20 bg-[#6BADA5]/5 p-3 text-xs text-white/60"
          >
            {selected === correctIndex
              ? "Верно! Outbox коммитит событие в одной транзакции с доменным стейтом — после теста сразу виден разбор."
              : "Правильный ответ подсвечен. После каждого вопроса открывается разбор — так пробелы видно сразу, а не на собесе."}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Mockup registry
// ---------------------------------------------------------------------------

const mockupComponents: Partial<Record<string, React.ComponentType<{ isActive: boolean }>>> = {
  videos: MockupVideoPlayer,
  practice: MockupTaskAssignment,
  tests: MockupQuiz,
  review: MockupCodeReview,
  community: MockupCommunity,
  platform: MockupPlatform,
};

export function LiveMockup({ id, isActive = true }: { id: string; isActive?: boolean }) {
  const Component = mockupComponents[id];
  if (!Component) return null;
  return (
    <div className="overflow-hidden rounded-2xl border border-white/[0.06] bg-[#141416] transition-all duration-300 hover:border-[rgba(107,173,165,0.3)]">
      <Component isActive={isActive} />
    </div>
  );
}

export { mockupComponents };
