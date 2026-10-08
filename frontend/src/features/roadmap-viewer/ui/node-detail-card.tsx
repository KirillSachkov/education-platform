"use client";

import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { ENTITY_ICONS, ENTITY_BG_COLORS, ENTITY_LABELS } from "@/shared/config/entity-icons";
import type { ProgressColor } from "@/entities/roadmap";
import { BookOpen, ExternalLink, HelpCircle, LayoutList, X } from "lucide-react";
import { ContentImage } from "@/shared/ui/components";
import { useEffect, useRef, useState } from "react";

const NODE_ICONS: Record<string, typeof HelpCircle> = {
  Course: ENTITY_ICONS.course,
  Module: ENTITY_ICONS.module,
  Lesson: ENTITY_ICONS.lesson,
  Project: ENTITY_ICONS.project,
  Issue: ENTITY_ICONS.issue,
  Article: BookOpen,
  Quiz: HelpCircle,
};

const NODE_BG: Record<string, string> = {
  Course: ENTITY_BG_COLORS.course,
  Module: ENTITY_BG_COLORS.module,
  Lesson: ENTITY_BG_COLORS.lesson,
  Project: ENTITY_BG_COLORS.project,
  Issue: ENTITY_BG_COLORS.issue,
  Article: "bg-emerald-500",
  Quiz: "bg-amber-500",
};

const NODE_LABEL: Record<string, string> = {
  Course: ENTITY_LABELS.course,
  Module: ENTITY_LABELS.module,
  Lesson: ENTITY_LABELS.lesson,
  Project: ENTITY_LABELS.project,
  Issue: ENTITY_LABELS.issue,
  Article: "Статья",
  Quiz: "Тест",
};

const PROGRESS_LABELS: Record<ProgressColor, string> = {
  completed: "Завершено",
  "in-progress": "В процессе",
  "not-started": "Не начато",
  "not-enrolled": "Не записан",
  default: "",
};

const PROGRESS_BADGE: Record<ProgressColor, string> = {
  completed: "bg-green-500/20 text-green-400",
  "in-progress": "bg-yellow-500/20 text-yellow-400",
  "not-started": "bg-muted text-muted-foreground",
  "not-enrolled": "bg-muted text-muted-foreground/60",
  default: "",
};

export interface NodeDetailData {
  entityType: string;
  entityId: string;
  entityTitle?: string | null;
  entityDescription?: string | null;
  imageId?: string | null;
  progressColor: ProgressColor;
}

interface NodeDetailCardProps {
  data: NodeDetailData;
  position: { x: number; y: number };
  onNavigate: () => void;
  onClose: () => void;
}

export function NodeDetailCard({ data, position, onNavigate, onClose }: NodeDetailCardProps) {
  const cardRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  // Adjust card position to stay within viewport
  useEffect(() => {
    const card = cardRef.current;
    if (!card) return;
    const rect = card.getBoundingClientRect();
    if (rect.right > window.innerWidth - 16) {
      // Try flipping to the left of the node, then clamp to the viewport edge.
      const flipped = position.x - rect.width - 20;
      card.style.left = `${Math.max(16, Math.min(flipped, window.innerWidth - rect.width - 16))}px`;
    }
    if (rect.bottom > window.innerHeight - 16) {
      card.style.top = `${Math.max(16, position.y - rect.height)}px`;
    }
  }, [position]);

  const [imgError, setImgError] = useState(false);
  const [trackedImageId, setTrackedImageId] = useState(data.imageId);
  if (trackedImageId !== data.imageId) {
    setTrackedImageId(data.imageId);
    setImgError(false);
  }
  const Icon = NODE_ICONS[data.entityType] ?? LayoutList;
  const iconBg = NODE_BG[data.entityType] ?? "bg-muted";
  const typeLabel = NODE_LABEL[data.entityType] ?? data.entityType;
  const progressLabel = PROGRESS_LABELS[data.progressColor];
  const progressBadge = PROGRESS_BADGE[data.progressColor];
  const imageUrl = data.imageId && !imgError ? `/api/files/${data.imageId}/content` : null;

  return (
    <div
      ref={cardRef}
      className="absolute z-50 w-[min(300px,calc(100vw-1.5rem))] animate-in fade-in zoom-in-95 duration-150 rounded-xl border border-border bg-card shadow-2xl backdrop-blur-md"
      style={{ left: position.x + 20, top: position.y }}
      onClick={(e) => e.stopPropagation()}
    >
      {/* Header with image or gradient */}
      {imageUrl ? (
        <div className="relative h-32 w-full overflow-hidden rounded-t-xl">
          <ContentImage
            src={imageUrl}
            alt={data.entityTitle ?? ""}
            fill
            sizes="300px"
            className="object-cover"
            onError={() => setImgError(true)}
          />
          <div className="absolute inset-0 bg-gradient-to-t from-card to-transparent" />
        </div>
      ) : (
        <div
          className={cn(
            "h-16 w-full rounded-t-xl bg-gradient-to-br from-primary/15 via-primary/5 to-transparent",
          )}
        />
      )}

      {/* Close button */}
      <button
        type="button"
        onClick={onClose}
        className="absolute right-2 top-2 rounded-full bg-card/80 p-1 text-muted-foreground hover:text-foreground backdrop-blur-sm transition-colors"
      >
        <X className="size-4" />
      </button>

      {/* Content */}
      <div className="p-4 -mt-4 relative">
        <div className="flex items-center gap-2 mb-2">
          <div className={cn("flex size-8 items-center justify-center rounded-lg", iconBg)}>
            <Icon className="size-4 text-white" />
          </div>
          <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            {typeLabel}
          </span>
          {progressLabel && (
            <span
              className={cn("ml-auto rounded-full px-2 py-0.5 text-2xs font-medium", progressBadge)}
            >
              {progressLabel}
            </span>
          )}
        </div>

        <h3 className="text-base font-bold leading-snug mb-1">
          {data.entityTitle || "Без названия"}
        </h3>

        {data.entityDescription && (
          <p className="text-sm leading-relaxed text-muted-foreground line-clamp-4 mb-3">
            {data.entityDescription}
          </p>
        )}

        <Button onClick={onNavigate} className="w-full gap-2" size="sm">
          Перейти
          <ExternalLink className="size-3.5" />
        </Button>
      </div>
    </div>
  );
}
