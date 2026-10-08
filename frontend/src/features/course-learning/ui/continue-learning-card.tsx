"use client";

import Link from "next/link";
import type { CurriculumItemDto, CurriculumSectionDto } from "@/entities/course";
import { getMaterialKindBadge, type MaterialKind } from "@/entities/material";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { ContentImage } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";

interface ContinueLearningCardProps {
  item: CurriculumItemDto;
  section: CurriculumSectionDto;
  sectionNumber: number;
  courseSlug: string;
}

export function ContinueLearningCard({
  item,
  section,
  sectionNumber,
  courseSlug,
}: ContinueLearningCardProps) {
  const isMaterial = item.itemType === "Material";
  const isVideo = isMaterial && item.materialKind === "VIDEO";
  const FallbackIcon = isMaterial
    ? getMaterialKindBadge((item.materialKind as MaterialKind) ?? "ARTICLE").icon
    : Icons.issue;

  const href = isMaterial
    ? routes.courseMaterial(courseSlug, item.id)
    : routes.courseIssue(courseSlug, item.id);

  return (
    <Link
      href={href}
      onClick={() =>
        trackGrowthEvent({
          name: "continue_learning_click",
          properties: {
            target_type: isMaterial ? "material" : "issue",
            content_id: item.id,
          },
        })
      }
      className={cn(
        "group/continue relative block rounded-2xl border border-border/60 bg-card/40",
        "p-3 sm:p-4 transition-colors hover:border-primary/40 hover:bg-card/60",
      )}
    >
      <div className="flex items-center gap-4 sm:gap-5">
        <div className="relative shrink-0 w-28 h-16 sm:w-36 sm:h-20 rounded-xl overflow-hidden border border-border/40 bg-muted/40">
          {item.coverUrl ? (
            <ContentImage
              src={item.coverUrl}
              alt=""
              fill
              loading="eager"
              fetchPriority="high"
              sizes="144px"
              className="object-cover"
            />
          ) : (
            <div className="absolute inset-0 grid place-items-center">
              <FallbackIcon size={28} className="text-muted-foreground/55" strokeWidth={1.4} />
            </div>
          )}
          {isVideo && (
            <div
              className={cn(
                "absolute inset-0 grid place-items-center",
                item.coverUrl && "bg-gradient-to-t from-black/55 via-black/10 to-transparent",
              )}
            >
              <Icons.play
                size={26}
                className={item.coverUrl ? "text-white drop-shadow" : "text-muted-foreground/70"}
                fill="currentColor"
              />
            </div>
          )}
        </div>

        <div className="min-w-0 flex-1">
          <div className="text-[10px] uppercase tracking-[0.18em] font-semibold text-primary/85">
            Продолжить обучение
          </div>
          <p className="mt-1 text-xs sm:text-[13px] text-muted-foreground/85 line-clamp-1">
            <span className="tabular-nums">{sectionNumber}.</span> {section.title}
          </p>
          <h2 className="mt-1 text-base sm:text-lg font-semibold leading-snug text-foreground line-clamp-2">
            {item.title}
          </h2>
          {isMaterial && item.durationSeconds != null && (
            <p className="mt-1.5 text-xs text-muted-foreground/70 tabular-nums inline-flex items-center gap-1">
              <Icons.clock size={11} />
              {formatDurationSecondsHuman(item.durationSeconds)}
            </p>
          )}
        </div>

        <div
          className={cn(
            "shrink-0 hidden sm:inline-flex items-center gap-1.5 h-10 px-4 rounded-xl",
            "text-sm font-semibold bg-primary text-primary-foreground",
            "transition-all group-hover/continue:bg-primary/90 group-hover/continue:gap-2",
          )}
        >
          Продолжить
          <Icons.arrowRight size={16} />
        </div>
      </div>
    </Link>
  );
}
