"use client";

import Link from "next/link";
import type {
  CourseCurriculumDto,
  CurriculumItemDto,
  CurriculumSectionDto,
} from "@/entities/course";
import { canAccessItem, getCourseItemHref, type CourseAccessLevel } from "@/entities/course";
import type {
  CourseLearningStateDto,
  IssueLearningItemDto,
  IssueProgressStatus,
} from "@/entities/course-progress";
import { getMaterialKindBadge, type MaterialKind } from "@/entities/material";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { Icons } from "@/shared/ui/icons";

interface CurrentPositionCardProps {
  curriculum: CourseCurriculumDto;
  learningState: CourseLearningStateDto | null | undefined;
  accessLevel: CourseAccessLevel;
  courseSlug: string;
}

interface ItemMeta {
  item: CurriculumItemDto;
  section: CurriculumSectionDto;
  sectionIndex: number;
}

function formatRelative(iso: string): string {
  const then = new Date(iso).getTime();
  const diff = Date.now() - then;
  const minutes = Math.floor(diff / 60_000);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);
  if (minutes < 1) return "только что";
  if (minutes < 60) return `${minutes} мин назад`;
  if (hours < 24) return `${hours} ч назад`;
  if (days === 1) return "вчера";
  if (days < 7) return `${days} дн. назад`;
  return new Date(iso).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "short",
  });
}

function issueStatusLabel(status: IssueProgressStatus): string {
  switch (status) {
    case "COMPLETED":
      return "Выполнено";
    case "UNDER_REVIEW":
      return "На ревью";
    case "REQUESTED_CHANGES":
      return "Нужны правки";
    case "IN_PROGRESS":
      return "В работе";
    default:
      return "Начата";
  }
}

function buildItemLookup(curriculum: CourseCurriculumDto): Map<string, ItemMeta> {
  const lookup = new Map<string, ItemMeta>();
  const modules = curriculum.sections.filter((s) => s.itemType === "Module");
  modules.forEach((section, sectionIndex) => {
    for (const item of section.items) {
      if (item.itemType !== "Material" && item.itemType !== "Issue") continue;
      if (!lookup.has(item.id)) {
        lookup.set(item.id, { item, section, sectionIndex });
      }
    }
  });
  return lookup;
}

interface StopEntry {
  meta: ItemMeta;
  date: string;
  statusLabel: string;
  tone: "teal" | "orange" | "primary";
}

function pickLastPositionStop(
  learningState: CourseLearningStateDto,
  lookup: Map<string, ItemMeta>,
): StopEntry | null {
  if (!learningState.lastPosition) return null;
  const meta = lookup.get(learningState.lastPosition.entityId);
  if (!meta) return null;
  return {
    meta,
    date: learningState.lastPosition.openedAt,
    statusLabel: "Продолжить",
    tone: "primary",
  };
}

function pickLastMaterialStop(
  learningState: CourseLearningStateDto,
  lookup: Map<string, ItemMeta>,
): StopEntry | null {
  const last = [...(learningState.materials ?? [])]
    .filter((m) => m.status === "VIEWED" && m.viewedAt)
    .sort((a, b) => (b.viewedAt ?? "").localeCompare(a.viewedAt ?? ""))[0];
  if (!last?.viewedAt) return null;
  const meta = lookup.get(last.materialId);
  if (!meta) return null;
  return {
    meta,
    date: last.viewedAt,
    statusLabel: "Просмотрен",
    tone: "teal",
  };
}

function pickLastIssueStop(
  learningState: CourseLearningStateDto,
  lookup: Map<string, ItemMeta>,
): StopEntry | null {
  const last = [...learningState.issues]
    .filter((i) => i.startedAt || i.completedAt)
    .sort((a, b) => {
      const aTime = a.completedAt ?? a.startedAt ?? "";
      const bTime = b.completedAt ?? b.startedAt ?? "";
      return bTime.localeCompare(aTime);
    })[0] as IssueLearningItemDto | undefined;
  if (!last) return null;
  const date = last.completedAt ?? last.startedAt;
  if (!date) return null;
  const meta = lookup.get(last.issueId);
  if (!meta) return null;
  return {
    meta,
    date,
    statusLabel: issueStatusLabel(last.status),
    tone: "orange",
  };
}

export function CurrentPositionCard({
  curriculum,
  learningState,
  accessLevel,
  courseSlug,
}: CurrentPositionCardProps) {
  const modules = curriculum.sections.filter((s) => s.itemType === "Module");
  if (modules.length === 0 || !learningState) return null;

  const lookup = buildItemLookup(curriculum);
  const lastPosition = pickLastPositionStop(learningState, lookup);
  const lastMaterial = pickLastMaterialStop(learningState, lookup);
  const lastIssue = pickLastIssueStop(learningState, lookup);

  // Если ученик уже что-то открывал — показываем primary CTA на последнюю позицию.
  // Иначе fallback на исторические «последний просмотренный материал» и «последняя задача»
  // (нужны для legacy-юзеров, у которых last-position ещё нет).
  if (!lastPosition && !lastMaterial && !lastIssue) return null;

  return (
    <section>
      <div className="flex items-end justify-between gap-3 mb-3 sm:mb-4">
        <h2 className="text-sm sm:text-base md:text-lg font-semibold tracking-tight text-foreground">
          {lastPosition ? "Продолжить с того места, где остановились" : "Где вы остановились"}
        </h2>
        <Link
          href={routes.courseProgram(courseSlug)}
          className="text-xs sm:text-sm text-muted-foreground hover:text-primary transition-colors shrink-0 inline-flex items-center gap-1"
        >
          Вся программа
          <Icons.arrowRight size={14} />
        </Link>
      </div>

      {lastPosition ? (
        <StopEntryCard entry={lastPosition} accessLevel={accessLevel} courseSlug={courseSlug} />
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {lastMaterial && (
            <StopEntryCard
              entry={lastMaterial}
              accessLevel={accessLevel}
              courseSlug={courseSlug}
            />
          )}
          {lastIssue && (
            <StopEntryCard entry={lastIssue} accessLevel={accessLevel} courseSlug={courseSlug} />
          )}
        </div>
      )}
    </section>
  );
}

function StopEntryCard({
  entry,
  accessLevel,
  courseSlug,
}: {
  entry: StopEntry;
  accessLevel: CourseAccessLevel;
  courseSlug: string;
}) {
  const { meta, date, statusLabel, tone } = entry;
  const canOpen = canAccessItem(meta.item.accessType, accessLevel);
  const href = getCourseItemHref(courseSlug, meta.item.itemType, meta.item.id);

  const isMaterial = meta.item.itemType === "Material";
  const kindBadge =
    isMaterial && meta.item.materialKind
      ? getMaterialKindBadge(meta.item.materialKind as MaterialKind)
      : null;
  const KindIcon = kindBadge?.icon;

  const toneText =
    tone === "teal" ? "text-teal" : tone === "primary" ? "text-primary" : "text-orange";
  const issueIconClasses = "bg-orange/10 border border-orange/25 text-orange";
  const iconBlockClasses = kindBadge?.iconBgClassName ?? issueIconClasses;
  const chipLabel = kindBadge?.label ?? "Задание";
  const durationLabel =
    isMaterial && meta.item.durationSeconds != null
      ? formatDurationSecondsHuman(meta.item.durationSeconds)
      : null;

  const content = (
    <div className="flex items-start gap-3 rounded-xl p-4 border border-border/60 bg-card hover:border-primary/30 hover:bg-card/70 transition-colors h-full">
      <div
        className={cn(
          "size-10 shrink-0 rounded-xl flex items-center justify-center",
          iconBlockClasses,
        )}
      >
        {KindIcon ? <KindIcon size={16} /> : <Icons.issue size={16} />}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5 text-[10px] uppercase tracking-[0.14em] font-semibold">
          <span className={toneText}>{statusLabel}</span>
          <span className="text-muted-foreground/30">·</span>
          <span className="text-muted-foreground/70">{formatRelative(date)}</span>
        </div>
        <p className="text-sm font-semibold text-foreground mt-1 leading-snug line-clamp-2">
          {meta.item.title}
        </p>
        <div className="flex items-center gap-1.5 text-[11px] text-muted-foreground/80 mt-1.5 flex-wrap">
          <span
            className={cn(
              "inline-flex items-center gap-1 rounded-[4px] px-1.5 py-0.5 text-[9px] uppercase tracking-[0.12em] font-semibold border",
              kindBadge?.className ?? "border-orange/30 bg-orange/10 text-orange",
            )}
          >
            {KindIcon ? <KindIcon size={9} /> : <Icons.issue size={9} />}
            {chipLabel}
          </span>
          {durationLabel && (
            <>
              <span className="text-muted-foreground/30">·</span>
              <span className="tabular-nums">{durationLabel}</span>
            </>
          )}
          <span className="text-muted-foreground/30">·</span>
          <span className="truncate min-w-0">
            {String(meta.sectionIndex + 1).padStart(2, "0")}. {meta.section.title}
          </span>
        </div>
      </div>
    </div>
  );

  if (!canOpen) return content;
  return (
    <Link href={href} prefetch={false} className="block">
      {content}
    </Link>
  );
}
