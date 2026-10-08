"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import {
  canAccessItem,
  deriveLockReasonForItem,
  type CourseAccessLevel,
  type CurriculumItemDto,
  type CurriculumSectionDto,
} from "@/entities/course";
import { resolveLockCopy, resolveUnlockHref } from "@/shared/lib/lock-copy";
import type {
  CourseLearningStateDto,
  IssueProgressStatus,
  MaterialProgressStatus,
} from "@/entities/course-progress";
import { getMaterialKindBadge, type MaterialKind } from "@/entities/material";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { pluralize } from "@/shared/lib/pluralize";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/shared/ui/kit/collapsible";
import { Icons } from "@/shared/ui/icons";
import { ContentImage } from "@/shared/ui/components";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { getProgressColor, getProgressLabel } from "@/shared/ui/components/item-progress-indicator";

export type SectionItemFilter = "material" | "issue" | "quiz" | "all";

function passesFilter(item: CurriculumItemDto, filter: SectionItemFilter): boolean {
  if (filter === "material") return item.itemType === "Material";
  if (filter === "issue") return item.itemType === "Issue";
  if (filter === "quiz") return item.itemType === "Quiz";
  return item.itemType === "Material" || item.itemType === "Issue" || item.itemType === "Quiz";
}

export type SectionKind = "module" | "project" | "getting-started";

interface SectionStats {
  completed: number;
  total: number;
  percent: number;
}

interface CurriculumSectionCardProps {
  section: CurriculumSectionDto;
  sectionNumber: number;
  sectionKind: SectionKind;
  itemFilter: SectionItemFilter;
  learningState: CourseLearningStateDto | null | undefined;
  accessLevel: CourseAccessLevel;
  courseSlug: string;
  defaultOpen: boolean;
  isHighlighted?: boolean;
  currentPath?: string | null;
  sectionRef?: (el: HTMLDivElement | null) => void;
  renderItemTrailing?: (item: CurriculumItemDto) => ReactNode;
  lastPositionItemId?: string | null;
}

function getMaterialStatus(
  itemId: string,
  learningState: CourseLearningStateDto | null | undefined,
): MaterialProgressStatus {
  return learningState?.materials.find((m) => m.materialId === itemId)?.status ?? "NOT_VIEWED";
}

function getIssueStatus(
  itemId: string,
  learningState: CourseLearningStateDto | null | undefined,
): IssueProgressStatus {
  return learningState?.issues.find((i) => i.issueId === itemId)?.status ?? "NOT_STARTED";
}

// Для quiz-итемов id = reference_id module_item'а = id самого квиза (ST-16 #495).
function isQuizPassed(
  itemId: string,
  learningState: CourseLearningStateDto | null | undefined,
): boolean {
  return learningState?.passedQuizIds?.includes(itemId) ?? false;
}

function isItemCompleted(
  item: CurriculumItemDto,
  learningState: CourseLearningStateDto | null | undefined,
): boolean {
  if (item.itemType === "Material") {
    return getMaterialStatus(item.id, learningState) === "VIEWED";
  }
  if (item.itemType === "Issue") {
    return getIssueStatus(item.id, learningState) === "COMPLETED";
  }
  if (item.itemType === "Quiz") {
    return isQuizPassed(item.id, learningState);
  }
  return false;
}

function computeStats(
  items: CurriculumItemDto[],
  filter: SectionItemFilter,
  learningState: CourseLearningStateDto | null | undefined,
): SectionStats | null {
  const targets = items.filter((i) => passesFilter(i, filter));
  if (targets.length === 0) return null;

  let completed = 0;
  for (const item of targets) {
    if (isItemCompleted(item, learningState)) completed++;
  }
  return {
    completed,
    total: targets.length,
    percent: Math.round((completed / targets.length) * 100),
  };
}

const ISSUE_BADGE_CLASS = "border-orange/30 bg-orange/10 text-orange";
// Violet — единый цвет quiz-сущностей (зеркало CollectionQuizTimelineRow, ST-16 #495).
const QUIZ_BADGE_CLASS = "border-violet-500/30 bg-violet-500/10 text-violet-400";

export function CurriculumSectionCard({
  section,
  sectionNumber,
  sectionKind,
  itemFilter,
  learningState,
  accessLevel,
  courseSlug,
  defaultOpen,
  isHighlighted,
  currentPath,
  sectionRef,
  renderItemTrailing,
  lastPositionItemId,
}: CurriculumSectionCardProps) {
  const items = section.items.filter((i) => passesFilter(i, itemFilter));
  const stats = computeStats(section.items, itemFilter, learningState);
  const isCompleted = stats !== null && stats.completed === stats.total;
  const totalDurationSec = items.reduce(
    (sum, i) => sum + (i.itemType === "Material" && i.durationSeconds ? i.durationSeconds : 0),
    0,
  );

  const sectionLabel = String(sectionNumber).padStart(2, "0");

  return (
    <Collapsible defaultOpen={defaultOpen} className="group">
      <div
        ref={sectionRef}
        id={`curriculum-section-${section.id}`}
        className={cn(
          "scroll-mt-20 rounded-xl border border-border/50 bg-card/30 overflow-hidden transition-all",
          "group-data-[state=open]:border-border/80 group-data-[state=open]:bg-card/40",
          isHighlighted && "ring-2 ring-primary/20 ring-offset-2 ring-offset-background",
          sectionKind === "getting-started" && "border-teal/25",
        )}
      >
        <CollapsibleTrigger asChild>
          <button
            type="button"
            className="w-full text-left flex items-center gap-3 px-3 sm:px-4 py-2.5 sm:py-3 hover:bg-muted/20 transition-colors"
          >
            <span
              className={cn(
                "shrink-0 inline-flex items-center justify-center w-8 h-8 rounded-lg",
                "text-xs sm:text-sm font-bold tabular-nums",
                "border border-border/60 bg-background/50 text-foreground/70",
                isCompleted && "border-green/40 bg-green/10 text-green",
                sectionKind === "getting-started" &&
                  !isCompleted &&
                  "border-teal/40 bg-teal/10 text-teal",
              )}
            >
              {sectionLabel}
            </span>

            <div className="min-w-0 flex-1">
              <h3
                className={cn(
                  "text-sm sm:text-base font-semibold leading-tight tracking-tight text-foreground truncate",
                  isCompleted && "text-muted-foreground",
                )}
              >
                {section.title}
              </h3>
              {sectionKind === "getting-started" && (
                <span className="mt-0.5 inline-flex items-center gap-1 px-1.5 py-0.5 rounded-[4px] text-[9px] uppercase tracking-[0.14em] font-semibold bg-teal/15 text-teal border border-teal/25">
                  <Icons.compass size={9} />
                  Старт
                </span>
              )}
            </div>

            <div className="flex items-center gap-2.5 sm:gap-3 shrink-0">
              {stats && (
                <>
                  <span className="hidden sm:inline-flex items-center justify-center">
                    <SectionProgressDial percent={stats.percent} isCompleted={isCompleted} />
                  </span>
                  <span
                    className={cn(
                      "text-xs sm:text-sm tabular-nums font-medium",
                      isCompleted ? "text-green" : "text-muted-foreground",
                    )}
                  >
                    {stats.completed}/{stats.total}
                  </span>
                </>
              )}
              {totalDurationSec > 0 && (
                <span className="hidden md:inline-flex items-center gap-1 text-[11px] tabular-nums text-muted-foreground/70">
                  <Icons.clock size={10} />
                  {formatDurationSecondsHuman(totalDurationSec)}
                </span>
              )}
              <Icons.chevronDown
                size={16}
                className="text-muted-foreground/60 transition-transform group-data-[state=open]:rotate-180"
              />
            </div>
          </button>
        </CollapsibleTrigger>

        <CollapsibleContent>
          <div className="border-t border-border/50">
            {sectionKind === "project" && (section.detailedDescription || section.description) && (
              <ProjectAboutBlock
                description={section.description}
                detailedDescription={section.detailedDescription}
              />
            )}
            {items.length === 0 ? (
              <p className="text-xs text-muted-foreground py-3 px-5">Нет доступных элементов</p>
            ) : (
              <ItemList
                items={items}
                sectionNumber={sectionNumber}
                learningState={learningState}
                accessLevel={accessLevel}
                courseSlug={courseSlug}
                currentPath={currentPath}
                renderItemTrailing={renderItemTrailing}
                lastPositionItemId={lastPositionItemId}
              />
            )}
          </div>
        </CollapsibleContent>
      </div>
    </Collapsible>
  );
}

function ProjectAboutBlock({
  description,
  detailedDescription,
}: {
  description: string | null;
  detailedDescription: string | null;
}) {
  return (
    <Collapsible defaultOpen className="group/about border-b border-border/50 bg-muted/10">
      <CollapsibleTrigger asChild>
        <button
          type="button"
          className="w-full flex items-center gap-2 px-3 sm:px-4 py-2.5 text-left hover:bg-muted/20 transition-colors"
        >
          <Icons.info size={14} className="shrink-0 text-primary/70" />
          <span className="text-xs font-semibold uppercase tracking-[0.1em] text-muted-foreground">
            О проекте
          </span>
          <Icons.chevronDown
            size={14}
            className="ml-auto text-muted-foreground/60 transition-transform group-data-[state=open]/about:rotate-180"
          />
        </button>
      </CollapsibleTrigger>
      <CollapsibleContent>
        <div className="px-3 sm:px-4 pb-4 pt-1">
          {detailedDescription ? (
            <MarkdownContent variant="full" className="text-sm">
              {detailedDescription}
            </MarkdownContent>
          ) : (
            <p className="text-sm text-muted-foreground whitespace-pre-line">{description}</p>
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

function SectionProgressDial({ percent, isCompleted }: { percent: number; isCompleted: boolean }) {
  const size = 22;
  const stroke = 2.5;
  const r = (size - stroke) / 2;
  const c = 2 * Math.PI * r;
  const offset = c * (1 - percent / 100);

  if (isCompleted) {
    return (
      <span className="inline-flex items-center justify-center w-[22px] h-[22px] rounded-full bg-green/15 border border-green/40">
        <Icons.check size={12} strokeWidth={3} className="text-green" />
      </span>
    );
  }

  return (
    <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} className="-rotate-90">
      <circle
        cx={size / 2}
        cy={size / 2}
        r={r}
        fill="none"
        strokeWidth={stroke}
        className="stroke-muted-foreground/25"
      />
      {percent > 0 && (
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          strokeWidth={stroke}
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={offset}
          className="stroke-primary transition-[stroke-dashoffset] duration-500"
        />
      )}
    </svg>
  );
}

function ItemList({
  items,
  sectionNumber,
  learningState,
  accessLevel,
  courseSlug,
  currentPath,
  renderItemTrailing,
  lastPositionItemId,
}: {
  items: CurriculumItemDto[];
  sectionNumber: number;
  learningState: CourseLearningStateDto | null | undefined;
  accessLevel: CourseAccessLevel;
  courseSlug: string;
  currentPath?: string | null;
  renderItemTrailing?: (item: CurriculumItemDto) => ReactNode;
  lastPositionItemId?: string | null;
}) {
  return (
    <ul className="divide-y divide-border/40">
      {items.map((item, index) => {
        const isMaterial = item.itemType === "Material";
        const isQuiz = item.itemType === "Quiz";
        const canOpen = canAccessItem(item.accessType, accessLevel);
        const lockReason = canOpen ? null : deriveLockReasonForItem(item.accessType, accessLevel);
        const lockHint = lockReason ? resolveLockCopy(lockReason).shortHint : null;
        // Квиз маппится на бинарный статус (passed → COMPLETED) — переиспользует
        // те же индикатор/цвета, что материалы/задания. getProgressLabel для
        // COMPLETED/NOT_STARTED возвращает null, отдельной ветки не нужно.
        const status = isMaterial
          ? getMaterialStatus(item.id, learningState)
          : isQuiz
            ? isQuizPassed(item.id, learningState)
              ? "COMPLETED"
              : "NOT_STARTED"
            : getIssueStatus(item.id, learningState);
        const completed = status === "VIEWED" || status === "COMPLETED";
        const isActive = item.id === lastPositionItemId && !completed;
        const progressLabel = canOpen ? getProgressLabel(status) : null;
        const progressColor = getProgressColor(status);

        const href = isMaterial
          ? routes.courseMaterial(courseSlug, item.id)
          : isQuiz
            ? routes.courseQuiz(courseSlug, item.quizId ?? item.id)
            : routes.courseIssue(courseSlug, item.id);
        const unlockHref = canOpen
          ? null
          : resolveUnlockHref({
              lockReason,
              returnTo: currentPath ?? null,
            });

        const positionLabel = `${sectionNumber}.${index + 1}`;

        const rowBody = (
          <div
            className={cn(
              "flex items-center gap-3 px-3 sm:px-4 py-2 sm:py-2.5",
              "transition-colors",
              isActive && "bg-primary/[0.04]",
            )}
          >
            <StatusIndicator status={status} isActive={isActive} canOpen={canOpen} />

            {isMaterial && (
              <ItemThumbnail
                item={item}
                canOpen={canOpen}
                completed={completed}
                isActive={isActive}
              />
            )}

            <div className="min-w-0 flex-1 flex items-center gap-2 flex-wrap sm:flex-nowrap">
              <KindBadge item={item} />
              <p
                className={cn(
                  "min-w-0 text-sm leading-snug",
                  canOpen
                    ? completed
                      ? "text-muted-foreground/70 line-through decoration-muted-foreground/30"
                      : "text-foreground/90 group-hover/item:text-primary"
                    : "text-muted-foreground/60",
                  isActive && !completed && "text-foreground font-medium",
                )}
              >
                <span
                  className={cn(
                    "tabular-nums font-semibold mr-1.5",
                    canOpen ? "text-foreground" : "text-muted-foreground/50",
                    completed && "text-muted-foreground/60",
                  )}
                >
                  {positionLabel}.
                </span>
                <span className="break-words">{item.title}</span>
              </p>
              {item.isOptional && (
                <span className="shrink-0 text-[10px] uppercase tracking-[0.1em] font-semibold text-muted-foreground bg-muted/60 rounded px-1.5 py-0.5">
                  опц.
                </span>
              )}
            </div>

            <div className="flex items-center gap-2.5 sm:gap-3 shrink-0 text-[11px] text-muted-foreground/70">
              {isMaterial && item.durationSeconds != null && (
                <span className="tabular-nums hidden sm:inline">
                  {formatDurationSecondsHuman(item.durationSeconds)}
                </span>
              )}
              {isQuiz && item.questionsCount != null && (
                <span className="tabular-nums hidden sm:inline">
                  {item.questionsCount}{" "}
                  {pluralize(item.questionsCount, "вопрос", "вопроса", "вопросов")}
                </span>
              )}
              {progressLabel && (
                <span className={cn("font-medium hidden md:inline", progressColor)}>
                  {progressLabel}
                </span>
              )}
              {!canOpen && (
                <Icons.locked
                  size={13}
                  strokeWidth={1.6}
                  className="text-muted-foreground/50"
                  aria-label={lockHint ?? undefined}
                />
              )}
            </div>
          </div>
        );

        const linked = canOpen ? (
          <Link
            href={href}
            prefetch={false}
            className="block flex-1 hover:bg-muted/25 transition-colors"
          >
            {rowBody}
          </Link>
        ) : unlockHref ? (
          <Link
            href={unlockHref}
            prefetch={false}
            className="block flex-1 hover:bg-muted/25 transition-colors"
            title={lockHint ?? undefined}
            aria-label={lockHint ?? undefined}
          >
            {rowBody}
          </Link>
        ) : (
          <div
            className="block flex-1"
            title={lockHint ?? undefined}
            aria-label={lockHint ?? undefined}
          >
            {rowBody}
          </div>
        );

        return (
          <li key={item.id} className="group/item relative flex items-stretch">
            {linked}
            {renderItemTrailing && (
              <div className="shrink-0 flex items-center pr-2 sm:pr-3">
                {renderItemTrailing(item)}
              </div>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function StatusIndicator({
  status,
  isActive,
  canOpen,
}: {
  status: IssueProgressStatus | MaterialProgressStatus;
  isActive: boolean;
  canOpen: boolean;
}) {
  const completed = status === "VIEWED" || status === "COMPLETED";

  if (completed) {
    return (
      <span className="shrink-0 inline-flex items-center justify-center w-[18px] h-[18px] rounded-full bg-green/15 border border-green/45">
        <Icons.check size={11} strokeWidth={3} className="text-green" />
      </span>
    );
  }

  if (isActive) {
    return (
      <span className="shrink-0 inline-flex items-center justify-center w-[18px] h-[18px] rounded-full bg-primary border border-primary shadow-[0_0_8px_-1px] shadow-primary/60">
        <Icons.play
          size={8}
          strokeWidth={0}
          fill="currentColor"
          className="text-primary-foreground ml-[1px]"
        />
      </span>
    );
  }

  // In-progress / under-review / changes-requested — colored circle matching label
  if (status === "IN_PROGRESS") {
    return (
      <span className="shrink-0 inline-block w-[18px] h-[18px] rounded-full border-[1.5px] border-yellow bg-yellow/20" />
    );
  }
  if (status === "UNDER_REVIEW") {
    return (
      <span className="shrink-0 inline-block w-[18px] h-[18px] rounded-full border-[1.5px] border-blue bg-blue/20" />
    );
  }
  if (status === "REQUESTED_CHANGES") {
    return (
      <span className="shrink-0 inline-block w-[18px] h-[18px] rounded-full border-[1.5px] border-red bg-red/20" />
    );
  }

  return (
    <span
      className={cn(
        "shrink-0 inline-block w-[18px] h-[18px] rounded-full border-[1.5px]",
        canOpen ? "border-muted-foreground/40" : "border-muted-foreground/20",
      )}
      aria-hidden="true"
    />
  );
}

function ItemThumbnail({
  item,
  canOpen,
  completed,
  isActive,
}: {
  item: CurriculumItemDto;
  canOpen: boolean;
  completed: boolean;
  isActive: boolean;
}) {
  const isMaterial = item.itemType === "Material";
  const isVideo = isMaterial && item.materialKind === "VIDEO";
  const kindBadge = isMaterial
    ? getMaterialKindBadge((item.materialKind as MaterialKind) ?? "ARTICLE")
    : null;
  const FallbackIcon = kindBadge?.icon ?? Icons.issue;

  const ringColor = !canOpen
    ? "border-border/40"
    : isVideo
      ? "border-teal/30"
      : kindBadge
        ? "border-border/40"
        : "border-orange/30";

  return (
    <div
      className={cn(
        "relative shrink-0 w-20 h-12 sm:w-24 sm:h-14 rounded-md overflow-hidden border bg-muted/30",
        ringColor,
        isActive && "ring-1 ring-primary/40",
        !canOpen && "opacity-60 grayscale",
        completed && canOpen && "opacity-70",
      )}
      aria-hidden="true"
    >
      {item.coverUrl ? (
        <ContentImage
          src={item.coverUrl}
          alt=""
          fill
          loading="lazy"
          sizes="96px"
          className="object-cover"
        />
      ) : (
        <div
          className={cn("absolute inset-0 grid place-items-center", !isMaterial && "bg-orange/5")}
        >
          <FallbackIcon
            size={20}
            className={cn("text-muted-foreground/55", !isMaterial && "text-orange/70")}
            strokeWidth={1.6}
          />
        </div>
      )}
      {isVideo && item.coverUrl && (
        <div className="absolute inset-0 grid place-items-center bg-gradient-to-t from-black/55 via-black/10 to-transparent">
          <Icons.play size={20} className="text-white/95 drop-shadow" fill="currentColor" />
        </div>
      )}
      {isVideo && !item.coverUrl && (
        <div className="absolute inset-0 grid place-items-center">
          <Icons.play size={16} className="text-teal/70" fill="currentColor" />
        </div>
      )}
    </div>
  );
}

function KindBadge({ item }: { item: CurriculumItemDto }) {
  const isMaterial = item.itemType === "Material";
  if (isMaterial && item.materialKind) {
    const badge = getMaterialKindBadge(item.materialKind as MaterialKind);
    return (
      <span
        className={cn(
          "shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] uppercase tracking-[0.14em] font-semibold border",
          badge.className,
        )}
      >
        {badge.label}
      </span>
    );
  }
  if (item.itemType === "Quiz") {
    return (
      <span
        className={cn(
          "shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] uppercase tracking-[0.14em] font-semibold border",
          QUIZ_BADGE_CLASS,
        )}
      >
        Тест
      </span>
    );
  }
  return (
    <span
      className={cn(
        "shrink-0 inline-flex items-center px-1.5 py-0.5 rounded text-[10px] uppercase tracking-[0.14em] font-semibold border",
        ISSUE_BADGE_CLASS,
      )}
    >
      Задание
    </span>
  );
}
