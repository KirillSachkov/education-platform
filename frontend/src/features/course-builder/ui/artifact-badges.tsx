"use client";

import { Icons } from "@/shared/ui/icons";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/shared/ui/kit/tooltip";
import { cn } from "@/shared/lib/css";

/**
 * Три бейджа артефактов видео-материала в course-builder:
 *  - транскрипт (Captions)
 *  - таймкоды (ListVideo)
 *  - конспект / summary (NotebookText)
 *
 * Если у item нет ни одного из этих полей (не video или backend не обогатил) —
 * компонент возвращает `null`. Если поле отсутствует/null — иконка не рисуется
 * (а не "выключенный серый") — три полу-видимых иконки в каждом ряду делают
 * список визуально шумным, особенно когда видео без артефактов в начале курса.
 */
interface ArtifactBadgesProps {
  hasTranscript?: boolean | null;
  hasTimecodes?: boolean | null;
  hasSummary?: boolean | null;
  className?: string;
}

export function ArtifactBadges({
  hasTranscript,
  hasTimecodes,
  hasSummary,
  className,
}: ArtifactBadgesProps) {
  const transcript = hasTranscript === true;
  const timecodes = hasTimecodes === true;
  const summary = hasSummary === true;

  if (!transcript && !timecodes && !summary) return null;

  return (
    <span className={cn("inline-flex items-center gap-1 shrink-0", className)}>
      {transcript && (
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="inline-flex items-center justify-center size-5 text-emerald-600/80 dark:text-emerald-400/80">
              <Icons.transcript size={13} />
            </span>
          </TooltipTrigger>
          <TooltipContent sideOffset={6}>Транскрипт готов</TooltipContent>
        </Tooltip>
      )}
      {timecodes && (
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="inline-flex items-center justify-center size-5 text-sky-600/80 dark:text-sky-400/80">
              <Icons.timecodes size={13} />
            </span>
          </TooltipTrigger>
          <TooltipContent sideOffset={6}>Таймкоды</TooltipContent>
        </Tooltip>
      )}
      {summary && (
        <Tooltip>
          <TooltipTrigger asChild>
            <span className="inline-flex items-center justify-center size-5 text-violet-600/80 dark:text-violet-400/80">
              <Icons.summary size={13} />
            </span>
          </TooltipTrigger>
          <TooltipContent sideOffset={6}>Конспект готов</TooltipContent>
        </Tooltip>
      )}
    </span>
  );
}
