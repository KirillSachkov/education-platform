"use client";

import {
  type GetVideoTimecodesResponse,
} from "@/entities/material-processing";
import { cn } from "@/shared/lib/css";
import { Badge } from "@/shared/ui/kit/badge";
import { Progress } from "@/shared/ui/kit/progress";
import {
  AlertCircle,
  CheckCircle2,
  FileText,
  ListVideo,
  Loader2,
} from "lucide-react";
import {
  getPrimaryProcessingJob,
  getStageLabel,
  getStatusLabel,
  hasActiveVideoProcessing,
  isProcessingActive,
} from "../lib/video-processing-ui";

interface VideoProcessingTopBarProps {
  status: GetVideoTimecodesResponse | undefined;
  isFetching?: boolean;
  isGeneratingTimecodes?: boolean;
  isGeneratingContent?: boolean;
  className?: string;
}

export function VideoProcessingTopBar({
  status,
  isFetching = false,
  isGeneratingTimecodes = false,
  isGeneratingContent = false,
  className,
}: VideoProcessingTopBarProps) {
  const primaryJob = getPrimaryProcessingJob(status);
  const active = hasActiveVideoProcessing(status);
  const activeTimecodes =
    isGeneratingTimecodes || isProcessingActive(status?.generation?.status);
  const activeContent =
    isGeneratingContent ||
    isProcessingActive(status?.activeContentGeneration?.status);
  // Show the FAILED banner ТОЛЬКО когда нет активной обработки. Иначе пред-
  // последний провалившийся timecode-job дублирует UI красной плашкой
  // «Обработка остановилась», пока пользователь спокойно ждёт текущий
  // конспект/транскрипт — выглядит как ошибка, хотя ничего не сломалось.
  const failedJob =
    !active && status?.generation?.status === "FAILED"
      ? status.generation
      : null;

  if (!status && !isFetching && !isGeneratingContent && !isGeneratingTimecodes) {
    return null;
  }

  if (!active && !failedJob) {
    return null;
  }

  const progress = Math.max(
    0,
    Math.min(
      100,
      primaryJob?.progressPercent ??
        (status?.hasTranscript ? 100 : isFetching ? 8 : 0),
    ),
  );

  const title = failedJob
    ? "Обработка остановилась"
    : getStageLabel(primaryJob?.stage);

  const description = failedJob
    ? failedJob.errorMessage ?? "Не удалось завершить обработку видео"
    : getStatusLabel(primaryJob?.status);

  return (
    <div
      className={cn(
        "rounded-lg border border-border/60 bg-background/60 px-3 py-2.5",
        failedJob && "border-destructive/30 bg-destructive/5",
        className,
      )}
    >
      <div className="flex w-full flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex min-w-0 items-center gap-2">
            <div
              className={cn(
                "flex size-8 shrink-0 items-center justify-center rounded-lg border",
                failedJob
                  ? "border-destructive/30 bg-destructive/10 text-destructive"
                  : active
                    ? "border-primary/30 bg-primary/10 text-primary"
                    : "border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
              )}
            >
              {failedJob ? (
                <AlertCircle size={16} />
              ) : active ? (
                <Loader2 size={16} className="animate-spin" />
              ) : (
                <CheckCircle2 size={16} />
              )}
            </div>
            <div className="min-w-0">
              <div className="flex min-w-0 flex-wrap items-center gap-2">
                <span className="text-sm font-medium">AI обработка видео</span>
                <span className="min-w-0 truncate text-sm text-muted-foreground">
                  {title}
                </span>
              </div>
              <p className="truncate text-xs text-muted-foreground">
                {description}
              </p>
            </div>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-1.5">
            {activeTimecodes && (
              <Badge variant="secondary" className="gap-1">
                <ListVideo size={12} />
                Главы
              </Badge>
            )}
            {activeContent && (
              <Badge variant="secondary" className="gap-1">
                <FileText size={12} />
                Конспект
              </Badge>
            )}
            {/* На failed не показываем % — это не прогресс, а место остановки. */}
            {!failedJob && (
              <span className="w-10 text-right text-xs tabular-nums text-muted-foreground">
                {progress}%
              </span>
            )}
          </div>
        </div>
        {/* Прогресс-бар только когда активный job. На failed — не показываем,
            красная плашка с error-message сама достаточно информативна. */}
        {active && !failedJob && (
          <Progress value={progress} className="h-1.5" />
        )}
      </div>
    </div>
  );
}
