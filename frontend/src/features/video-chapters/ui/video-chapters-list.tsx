"use client";

import type { MaterialChapterDto } from "@/entities/material";
import { cn } from "@/shared/lib/css";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { formatTimecode } from "./format-timecode";

interface VideoChaptersListProps {
  chapters: MaterialChapterDto[];
  /** Индекс активной главы (вычисляется по `currentTime` родителем); `-1` если ни одной. */
  activeIndex: number;
  onSeek: (seconds: number) => void;
  className?: string;
}

export function VideoChaptersList({
  chapters,
  activeIndex,
  onSeek,
  className,
}: VideoChaptersListProps) {
  if (chapters.length === 0) return null;

  return (
    <Card className={cn("gap-3 border-border/70 bg-card/80 p-4", className)}>
      <div className="flex items-center gap-2 text-sm font-semibold">
        <Icons.timecodes size={16} className="text-primary" />
        Главы <span className="text-muted-foreground">({chapters.length})</span>
      </div>
      <ol className="max-h-96 space-y-0.5 overflow-y-auto pr-1">
        {chapters.map((chapter, index) => {
          const isActive = index === activeIndex;
          return (
            <li key={`${chapter.timeSeconds}-${index}`}>
              <button
                type="button"
                onClick={() => onSeek(chapter.timeSeconds)}
                className={cn(
                  "flex w-full items-baseline gap-3 rounded-lg px-2 py-1.5 text-left transition-colors",
                  isActive
                    ? "bg-primary/10 font-medium text-primary"
                    : "text-muted-foreground hover:bg-muted/50 hover:text-foreground",
                )}
              >
                <span className="w-6 shrink-0 text-right text-xs tabular-nums text-muted-foreground/60">
                  {index + 1}.
                </span>
                <span className="flex-1 text-sm leading-snug">{chapter.title}</span>
                <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
                  {formatTimecode(chapter.timeSeconds)}
                </span>
              </button>
            </li>
          );
        })}
      </ol>
    </Card>
  );
}
