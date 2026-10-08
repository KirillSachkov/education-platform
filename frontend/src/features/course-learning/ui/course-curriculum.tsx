"use client";

import {
  canAccessItem,
  deriveLockReasonForItem,
  getCourseItemHref,
  type CourseAccessLevel,
  type CourseViewTab,
  type CurriculumSectionDto,
} from "@/entities/course";
import {
  resolveLockCopy,
  resolveSecondaryUnlockHref,
  resolveUnlockHref,
} from "@/shared/lib/lock-copy";
import { LockCallout } from "@/shared/ui/components/lock-callout";
import { usePathname } from "next/navigation";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import type {
  CourseLearningStateDto,
  IssueProgressStatus,
  MaterialProgressStatus,
} from "@/entities/course-progress";
import { routes } from "@/shared/config/routes";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import {
  ItemProgressIndicator,
  getProgressLabel,
  getProgressColor,
} from "@/shared/ui/components/item-progress-indicator";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/shared/ui/kit/collapsible";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { Badge } from "@/shared/ui/kit/badge";
import { cn } from "@/shared/lib/css";
import { Check, ChevronRight, Compass, Lock } from "lucide-react";
import Link from "next/link";

interface CourseCurriculumProps {
  sections: CurriculumSectionDto[];
  accessLevel: CourseAccessLevel;
  learningState?: CourseLearningStateDto | null;
  activeTab?: CourseViewTab;
  gettingStartedModuleId?: string | null;
  /**
   * Опционально — заголовок курса используется в LockCallout для locked items.
   * Без него callout рендерит общую копию без названия курса.
   */
  courseTitle?: string | null;
}

function getSectionProgress(
  section: CurriculumSectionDto,
  learningState: CourseLearningStateDto | null | undefined,
) {
  if (!learningState) return null;

  const materialStatuses = new Map(
    (learningState.materials ?? []).map((m) => [m.materialId, m.status]),
  );
  const issueStatuses = new Map(
    learningState.issues.map((i) => [i.issueId, i.status]),
  );

  let completed = 0;
  let total = 0;

  for (const item of section.items) {
    if (item.itemType === "Material") {
      total++;
      if (materialStatuses.get(item.id) === "VIEWED") completed++;
    } else if (item.itemType === "Issue") {
      total++;
      if (issueStatuses.get(item.id) === "COMPLETED") completed++;
    }
  }

  if (total === 0) return null;
  return { completed, total, percent: Math.round((completed / total) * 100) };
}

function getItemStatus(
  item: { id: string; itemType: string },
  learningState: CourseLearningStateDto | null | undefined,
): IssueProgressStatus | MaterialProgressStatus {
  if (!learningState) {
    return item.itemType === "Issue" ? "NOT_STARTED" : "NOT_VIEWED";
  }
  if (item.itemType === "Material") {
    const found = learningState.materials?.find((m) => m.materialId === item.id);
    return found?.status ?? "NOT_VIEWED";
  }
  if (item.itemType === "Issue") {
    const found = learningState.issues.find((i) => i.issueId === item.id);
    return found?.status ?? "NOT_STARTED";
  }
  return "NOT_STARTED";
}

export function CourseCurriculum({
  sections,
  accessLevel,
  learningState,
  activeTab = "modules",
  gettingStartedModuleId,
  courseTitle,
}: CourseCurriculumProps) {
  const courseSlug = useCourseSlug();
  const returnTo = usePathname();
  return (
    <div className="space-y-2">
      {sections.map((section) => {
        const progress = getSectionProgress(section, learningState);
        const isSectionCompleted = progress !== null && progress.completed === progress.total;
        const isGettingStarted = section.id === gettingStartedModuleId;

        return (
          <Collapsible key={section.id} defaultOpen={!isSectionCompleted} className="group">
            <div className={cn("rounded-xl border bg-card", isGettingStarted && "border-primary/20")}>
              <div className="flex w-full items-center gap-3 p-4 hover:bg-muted/50 transition-colors rounded-xl">
                <CollapsibleTrigger className="shrink-0">
                  <ChevronRight
                    size={14}
                    className="text-muted-foreground transition-transform group-data-[state=open]:rotate-90"
                  />
                </CollapsibleTrigger>
                {isSectionCompleted && (
                  <Check size={14} strokeWidth={2.5} className="text-green shrink-0" />
                )}
                <Link
                  href={section.itemType === "Module" ? routes.courseModule(courseSlug, section.id) : routes.courseOverview(courseSlug)}
                  prefetch={false}
                  className={cn(
                    "text-sm font-semibold truncate flex-1 hover:underline",
                    isSectionCompleted && "text-muted-foreground",
                  )}
                >
                  {section.title}
                </Link>
                {isGettingStarted && (
                  <Badge variant="secondary" className="text-2xs shrink-0 gap-1">
                    <Compass size={10} />
                    Старт
                  </Badge>
                )}
                <div className="flex items-center gap-2 shrink-0">
                  {progress ? (
                    <span
                      className={cn(
                        "text-xs font-medium tabular-nums",
                        isSectionCompleted
                          ? "text-green"
                          : "text-muted-foreground",
                      )}
                    >
                      {isSectionCompleted ? "100%" : `${progress.percent}%`}
                    </span>
                  ) : (
                    <span className="text-2xs text-muted-foreground">
                      {formatRuPlural(section.items.length, RU_PLURALS.element)}
                    </span>
                  )}
                </div>
              </div>
              <CollapsibleContent>
                <div className="border-t px-4 pb-3">
                  {section.items.length === 0 ? (
                    <p className="text-xs text-muted-foreground py-3">
                      Нет доступных элементов
                    </p>
                  ) : (
                    <ul className="divide-y">
                      {section.items
                        .filter(
                          (item) =>
                            item.itemType === "Material" ||
                            item.itemType === "Issue",
                        )
                        .map((item, itemIndex) => {
                          const canOpenItem = canAccessItem(item.accessType, accessLevel);
                          const isLocked = !canOpenItem;
                          const lockReason = isLocked
                            ? deriveLockReasonForItem(item.accessType, accessLevel)
                            : null;
                          const lockHint = lockReason
                            ? resolveLockCopy(lockReason).shortHint
                            : null;
                          const status = getItemStatus(item, learningState);
                          const completed =
                            status === "VIEWED" || status === "COMPLETED";

                          const label = getProgressLabel(status);
                          const color = getProgressColor(status);

                          const content = (
                            <span className="flex items-center gap-3 py-2.5 text-sm transition-colors">
                              <span className="text-[10px] tabular-nums text-muted-foreground/40 w-4 text-right shrink-0">
                                {String(item.position).padStart(2, "0")}
                              </span>
                              <ItemProgressIndicator
                                itemType={
                                  item.itemType === "Issue"
                                    ? "issue"
                                    : "material"
                                }
                                status={status}
                                tier="standard"
                              />
                              <span
                                className={cn(
                                  "truncate transition-colors",
                                  isLocked && "text-muted-foreground",
                                  completed && "text-muted-foreground",
                                )}
                              >
                                {item.title}
                              </span>
                              {item.materialKind === "VIDEO" && !!item.durationSeconds && (
                                <span className="text-2xs tabular-nums text-muted-foreground/50 shrink-0">
                                  {formatDurationSecondsHuman(item.durationSeconds)}
                                </span>
                              )}
                              {label && (
                                <span className={cn("text-2xs shrink-0", color)}>
                                  {label}
                                </span>
                              )}
                              {item.viewPriority === "Supplementary" && (
                                <span className="text-[9px] text-muted-foreground bg-muted rounded-[3px] px-1.5 py-px shrink-0">
                                  Доп.
                                </span>
                              )}
                              {item.isOptional && (
                                <span className="text-[9px] text-muted-foreground bg-muted rounded-[3px] px-1.5 py-px shrink-0">
                                  опц.
                                </span>
                              )}
                              {isLocked && (
                                <Lock
                                  size={12}
                                  className="ml-auto shrink-0 text-muted-foreground"
                                />
                              )}
                            </span>
                          );

                          const unlockHref = isLocked
                            ? resolveUnlockHref({ lockReason, returnTo })
                            : null;
                          const secondaryHref = isLocked
                            ? resolveSecondaryUnlockHref({ lockReason })
                            : null;
                          return (
                            <li key={`${section.id}:${item.id}:${itemIndex}`}>
                              {isLocked ? (
                                <Popover>
                                  <PopoverTrigger asChild>
                                    <button
                                      type="button"
                                      className="block w-full text-left opacity-60 transition hover:opacity-100 hover:text-primary cursor-pointer"
                                      title={lockHint ?? undefined}
                                      aria-label={lockHint ?? undefined}
                                    >
                                      {content}
                                    </button>
                                  </PopoverTrigger>
                                  <PopoverContent
                                    align="start"
                                    sideOffset={8}
                                    collisionPadding={12}
                                    className="w-[min(20rem,calc(100vw-1.5rem))] rounded-2xl border-border/60 bg-card/95 p-4 shadow-xl shadow-black/20 backdrop-blur-xl"
                                  >
                                    <LockCallout
                                      reason={lockReason}
                                      courseTitle={courseTitle ?? null}
                                      ctaHref={unlockHref}
                                      secondaryCtaHref={secondaryHref}
                                    />
                                  </PopoverContent>
                                </Popover>
                              ) : (
                                <Link
                                  href={getCourseItemHref(
                                    courseSlug,
                                    item.itemType,
                                    item.id,
                                    { tab: activeTab },
                                  )}
                                  prefetch={false}
                                  className="hover:text-primary"
                                >
                                  {content}
                                </Link>
                              )}
                            </li>
                          );
                        })}
                    </ul>
                  )}
                </div>
              </CollapsibleContent>
            </div>
          </Collapsible>
        );
      })}
    </div>
  );
}
