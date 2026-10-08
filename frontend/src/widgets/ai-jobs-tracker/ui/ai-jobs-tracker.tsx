"use client";

import {
  type ActiveAiJobDto,
  type ActiveAiJobKind,
  materialProcessingQueryOptions,
} from "@/entities/material-processing";
import { useRoles } from "@/shared/auth/use-roles";
import { cn } from "@/shared/lib/css";
import { useIsMobile } from "@/shared/lib/react/use-mobile";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Progress } from "@/shared/ui/kit/progress";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useAiJobsLeaderPoll } from "../model/use-ai-jobs-leader-poll";
import { useAiJobsToasts } from "../model/use-ai-jobs-toasts";

const KIND_ICONS: Record<ActiveAiJobKind, IconComponent> = {
  TRANSCRIPT: Icons.document,
  TIMECODES: Icons.list,
  CONTENT: Icons.document,
};

const KIND_LABELS: Record<ActiveAiJobKind, string> = {
  TRANSCRIPT: "Транскрипт",
  TIMECODES: "Тайм-коды",
  CONTENT: "Конспект",
};

const STAGE_LABELS: Record<string, string> = {
  QUEUED: "В очереди",
  SOURCE_FETCH: "Загрузка источника",
  PROBE: "Анализ файла",
  AUDIO_EXTRACT: "Извлекаем аудио",
  TRANSCRIBE: "Распознаём речь",
  GENERATE: "AI генерация",
  SAVE: "Сохранение",
};

/**
 * Глобальный tracker active AI-job'ов автора. Подключается в `(app)/layout.tsx`,
 * виден на всех страницах. Сворачивается, скрывается когда нет active jobs.
 *
 * Polling — leader-only через `useAiJobsLeaderPoll` (#155): только одна tab
 * на user'а делает реальный HTTP-poll, остальные получают snapshots через
 * BroadcastChannel. 5s active / 30s idle dynamic interval.
 */
export function AiJobsTracker() {
  const { hasRole, isAtLeast } = useRoles();
  const enabled = isAtLeast("platform-author") || hasRole("platform-admin");
  const isMobile = useIsMobile();
  // На mobile collapsed-by-default — fixed bottom-right панель перекрывала
  // primary action buttons (FAB) и нижнюю safe-area. По tap'у header'а
  // юзер раскрывает list. На desktop default — expanded (привычное поведение).
  const [collapsed, setCollapsed] = useState(isMobile);

  // Leader-tab делает реальный poll; non-leader получает snapshot через
  // BroadcastChannel и пишет в cache → useQuery ниже подхватит автоматически.
  useAiJobsLeaderPoll(enabled);

  const { data } = useQuery({
    ...materialProcessingQueryOptions.activeJobs(),
    enabled,
  });

  const jobs = data?.jobs ?? [];
  // Toast'ы при transition active → completed/failed работают всегда (пока юзер
  // авторизован), даже если bar свёрнут.
  useAiJobsToasts(jobs, enabled);

  if (!enabled || jobs.length === 0) {
    return null;
  }

  return (
    <div
      className={cn(
        "pointer-events-none fixed bottom-4 z-50 flex flex-col gap-2",
        // Mobile — узкая centered панель чтобы не задирать ширину контента под ней.
        // Desktop — bottom-right с фикс. шириной.
        isMobile
          ? "left-1/2 w-[calc(100%-2rem)] max-w-xs -translate-x-1/2 items-center"
          : "right-4 w-full max-w-sm items-end",
      )}
      role="status"
      aria-live="polite"
    >
      <div className="pointer-events-auto w-full rounded-lg border border-border/60 bg-background/95 shadow-lg backdrop-blur supports-[backdrop-filter]:bg-background/85">
        <button
          type="button"
          onClick={() => setCollapsed((c) => !c)}
          className="flex w-full items-center justify-between gap-2 px-3 py-2.5 text-left"
        >
          <div className="flex items-center gap-2">
            <Icons.loading size={14} className="animate-spin text-primary" />
            <span className="text-sm font-medium">
              AI-обработка{" "}
              <span className="text-muted-foreground">({jobs.length})</span>
            </span>
          </div>
          <Icons.chevronDown
            size={14}
            className={cn(
              "text-muted-foreground transition-transform",
              collapsed && "rotate-180",
            )}
          />
        </button>

        {!collapsed && (
          <ul className="space-y-2 border-t border-border/40 px-3 py-2">
            {jobs.map((job) => (
              <li key={job.jobId}>
                <JobRow job={job} />
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function JobRow({ job }: { job: ActiveAiJobDto }) {
  const Icon = KIND_ICONS[job.jobKind];
  const stageLabel = STAGE_LABELS[job.stage] ?? job.stage;
  const progress = Math.max(0, Math.min(100, job.progressPercent));

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-center justify-between gap-2 text-xs">
        <div className="flex min-w-0 items-center gap-1.5">
          <Icon size={12} className="shrink-0 text-muted-foreground" />
          <span className="truncate font-medium">{KIND_LABELS[job.jobKind]}</span>
          <span className="truncate text-muted-foreground">{stageLabel}</span>
        </div>
        <span className="shrink-0 tabular-nums text-muted-foreground">
          {progress}%
        </span>
      </div>
      <Progress value={progress} className="h-1" />
    </div>
  );
}

