"use client";

import type { ModuleItemDto } from "@/entities/module";
import { VIEW_PRIORITIES, VIEW_PRIORITY_LABELS, type ViewPriority } from "@/entities/module";
import { routes } from "@/shared/config/routes";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";
import type { StatusType } from "@/shared/ui/components/status-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Button } from "@/shared/ui/kit/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import { useSortable } from "@dnd-kit/react/sortable";
import { ENTITY_ICONS, ENTITY_COLORS } from "@/shared/config/entity-icons";
import { Icons } from "@/shared/ui/icons";
import { pluralize } from "@/shared/lib/pluralize";
import { Check, GripVertical, MoreHorizontal, Pencil, Sparkles, Trash2 } from "lucide-react";
import Link from "next/link";
import { ReorderArrows } from "./reorder-arrows";
import {
  MaterialAccessTypeSubmenu,
  getAccessTypeShortLabel,
  type ContentAccessType,
} from "@/shared/ui/components";
import { useChangeMaterialAccessType } from "@/entities/material";
import type { MaterialAccessType } from "@/entities/material";

interface SortableModuleItemCardProps {
  item: ModuleItemDto;
  index: number;
  globalIndex?: number;
  moduleId: string;
  isFirst: boolean;
  isLast: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onViewPriorityChange: (viewPriority: string) => void;
}

export function SortableModuleItemCard({
  item,
  index,
  globalIndex,
  moduleId,
  isFirst,
  isLast,
  onMoveUp,
  onMoveDown,
  onEdit,
  onDelete,
  onViewPriorityChange,
}: SortableModuleItemCardProps) {
  const courseSlug = useCourseSlug();
  const courseId = useCourseId();
  const { ref, isDragging } = useSortable({
    id: item.referenceId,
    index,
    group: moduleId,
    type: "item",
    accept: ["item"],
  });

  const isSupplementary = item.viewPriority === "Supplementary";
  const isIssue = item.itemType === "Issue";
  const isMaterial = item.itemType === "Material";
  const isQuiz = item.itemType === "Quiz";

  const { setAccessType, isPending: isAccessPending } = useChangeMaterialAccessType();
  const handleAccessChange = (next: ContentAccessType) => {
    void setAccessType(item.referenceId, next);
  };

  // Материал редактируется на своей странице; квиз — в библиотеке /author/quizzes
  // (контент квиза в билдере не редактируется — он standalone, #494).
  const editHref = isMaterial
    ? routes.authorMaterialEdit(item.referenceId, { courseId, courseSlug })
    : isQuiz
      ? routes.authorQuizEdit(item.referenceId)
      : null;

  return (
    <div
      ref={ref}
      className={cn(
        "group/item flex items-center gap-2 sm:gap-3 px-2 sm:px-3 py-2.5 rounded-lg hover:bg-accent/30 transition-colors",
        isDragging && "opacity-50",
      )}
    >
      <GripVertical size={14} className="text-muted-foreground/40 cursor-grab shrink-0" />

      <ReorderArrows
        onMoveUp={onMoveUp}
        onMoveDown={onMoveDown}
        isFirst={isFirst}
        isLast={isLast}
        compact
      />

      {/* Position number */}
      <span className="text-[10px] tabular-nums text-muted-foreground/40 w-4 text-right shrink-0">
        {String(globalIndex ?? index + 1).padStart(2, "0")}
      </span>

      {isIssue ? (
        <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-orange-500/15">
          <ENTITY_ICONS.issue size={12} className={ENTITY_COLORS.issue} />
        </span>
      ) : isQuiz ? (
        <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-violet-500/15">
          <Icons.quiz size={12} className="text-violet-500" />
        </span>
      ) : item.coverUrl ? (
        // eslint-disable-next-line @next/next/no-img-element -- third-party CDN (Kinescope/MinIO), no Next/Image domain config needed
        <img
          src={item.coverUrl}
          alt=""
          loading="lazy"
          className="h-6 w-10 shrink-0 rounded-md object-cover bg-muted"
        />
      ) : (
        <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-teal-500/15">
          <ENTITY_ICONS.lesson size={12} className={ENTITY_COLORS.lesson} />
        </span>
      )}

      {/* Title — material/quiz → edit page (материал — своя страница, квиз — библиотека);
          issue → opens edit sheet via onEdit */}
      {editHref ? (
        <Link
          href={editHref}
          prefetch={false}
          className={cn(
            "text-sm truncate flex-1 hover:underline",
            isSupplementary && "text-muted-foreground",
          )}
        >
          {item.title ?? "Без названия"}
        </Link>
      ) : (
        <button
          type="button"
          onClick={onEdit}
          className={cn(
            "text-sm truncate flex-1 hover:underline text-left bg-transparent p-0 cursor-pointer",
            isSupplementary && "text-muted-foreground",
          )}
        >
          {item.title ?? "Без названия"}
        </button>
      )}

      {/* Priority badge — shown for non-default (Recommended, Supplementary) */}
      {item.viewPriority === "Recommended" && (
        <span className="text-[9px] text-primary/60 bg-primary/8 rounded-[3px] px-1.5 py-px shrink-0">
          Рек.
        </span>
      )}
      {isSupplementary && (
        <span className="text-[9px] text-muted-foreground/50 bg-muted rounded-[3px] px-1.5 py-px shrink-0">
          Доп.
        </span>
      )}

      {isQuiz && item.questionsCount != null && (
        <span className="hidden sm:inline text-[10px] tabular-nums text-muted-foreground/70 shrink-0">
          {item.questionsCount} {pluralize(item.questionsCount, "вопрос", "вопроса", "вопросов")}
        </span>
      )}

      <span className="hidden sm:inline-flex">
        {item.status && <StatusBadge status={item.status as StatusType} />}
      </span>

      {/* Context menu — edit, priority, delete */}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            className="size-7 text-muted-foreground hover:text-foreground shrink-0 sm:opacity-0 sm:group-hover/item:opacity-100 transition-opacity"
            aria-label="Действия"
          >
            <MoreHorizontal size={14} />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {editHref ? (
            <DropdownMenuItem asChild>
              <Link href={editHref} prefetch={false}>
                <Pencil size={14} />
                Редактировать
              </Link>
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem onClick={onEdit}>
              <Pencil size={14} />
              Редактировать
            </DropdownMenuItem>
          )}
          <DropdownMenuSub>
            <DropdownMenuSubTrigger>
              <Sparkles size={14} />
              Приоритет
            </DropdownMenuSubTrigger>
            <DropdownMenuSubContent>
              {VIEW_PRIORITIES.map((p) => (
                <DropdownMenuItem key={p} onClick={() => onViewPriorityChange(p)}>
                  {item.viewPriority === p && <Check size={14} className="text-primary" />}
                  <span className={item.viewPriority !== p ? "pl-5" : ""}>
                    {VIEW_PRIORITY_LABELS[p as ViewPriority]}
                  </span>
                </DropdownMenuItem>
              ))}
            </DropdownMenuSubContent>
          </DropdownMenuSub>
          {isMaterial && (
            <MaterialAccessTypeSubmenu
              value={(item.accessType as MaterialAccessType) ?? "PUBLIC"}
              onChange={handleAccessChange}
              disabled={isAccessPending}
              triggerLabel={`Доступ: ${getAccessTypeShortLabel((item.accessType as MaterialAccessType) ?? "PUBLIC")}`}
            />
          )}
          <DropdownMenuSeparator />
          <DropdownMenuItem className="text-red" onClick={onDelete}>
            <Trash2 size={14} />
            Удалить
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
