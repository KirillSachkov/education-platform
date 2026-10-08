"use client";

import type { BuilderSectionDto } from "@/entities/course";
import type { ModuleItemDto } from "@/entities/module";
import { cn } from "@/shared/lib/css";
import { compareSortKey } from "@/shared/lib/sort-key";
import type { StatusType } from "@/shared/ui/components/status-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import { useDroppable } from "@dnd-kit/react";
import { useSortable } from "@dnd-kit/react/sortable";
import {
  Archive,
  ArchiveRestore,
  FileText,
  GripVertical,
  MoreHorizontal,
  Pencil,
  Plus,
  Trash2,
} from "lucide-react";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { Icons } from "@/shared/ui/icons";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { useArchiveModule } from "../model/use-archive-module";
import { useDetachModuleItem } from "../model/use-detach-module-item";
import { useRestoreModule } from "../model/use-restore-module";
import { useUpdateViewPriority } from "../model/use-update-view-priority";
import { EditIssueSheet } from "./edit-issue-sheet";
import { ModuleStatusActions } from "./module-status-actions";
import { ReorderArrows } from "./reorder-arrows";
import { SortableModuleItemCard } from "./sortable-module-item-card";

interface ModuleCardProps {
  module: BuilderSectionDto;
  items: ModuleItemDto[];
  description: string | null;
  detailedDescription: string | null;
  index: number;
  courseId: string;
  globalItemOffset?: number;
  onEdit: (detail: {
    title: string;
    description: string | null;
    detailedDescription: string | null;
  }) => void;
  onDetach: () => void;
  isDetachPending: boolean;
  onAddMaterial: () => void;
  onAddQuiz: () => void;
  onAttachIssue: () => void;
  isAttachIssuePending: boolean;
  /** Touch reorder fallback for the module itself (recipe 7). */
  isFirst: boolean;
  isLast: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  /** Touch reorder fallback for items within this module. */
  onMoveItem: (referenceId: string, fromIndex: number, toIndex: number) => void;
}

export function ModuleCard({
  module,
  items: itemsProp,
  description,
  detailedDescription,
  index,
  courseId,
  onEdit,
  onDetach,
  isDetachPending,
  onAddMaterial,
  onAddQuiz,
  onAttachIssue,
  isAttachIssuePending,
  globalItemOffset = 0,
  isFirst,
  isLast,
  onMoveUp,
  onMoveDown,
  onMoveItem,
}: ModuleCardProps) {
  const courseSlug = useCourseSlug();
  const { ref, isDragging } = useSortable({
    id: module.id,
    index,
    group: "sections",
    type: "module",
    accept: ["module"],
  });

  const { ref: dropZoneRef, isDropTarget } = useDroppable({
    id: `drop-zone-${module.id}`,
    accept: ["item"],
  });

  const detachModuleItem = useDetachModuleItem(courseId, module.id);
  const { archiveModule, isPending: isArchivePending } = useArchiveModule(courseId);
  const { restoreModule, isPending: isRestorePending } = useRestoreModule(courseId);
  const { updateViewPriority } = useUpdateViewPriority(courseId, module.id);
  const router = useRouter();

  const [editIssueId, setEditIssueId] = useState<string | null>(null);

  const items = [...itemsProp].sort((a, b) => compareSortKey(a.sortKey, b.sortKey));

  return (
    <>
      <Card
        ref={ref}
        data-dragging={isDragging}
        className={cn("p-0 gap-0 overflow-hidden", isDragging && "opacity-50")}
      >
        <div className="group flex items-center gap-2 sm:gap-3 px-3 sm:px-5 py-3 sm:py-4">
          <GripVertical size={14} className="text-muted-foreground/40 cursor-grab shrink-0" />
          <span className="text-xs font-bold tabular-nums text-muted-foreground/50 w-5 text-right shrink-0">
            {index + 1}
          </span>
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <span className="text-sm font-semibold truncate">
                {module.title ?? "Без названия"}
              </span>
              <span className="hidden sm:inline-flex shrink-0">
                {module.status && <StatusBadge status={module.status as StatusType} />}
              </span>
            </div>
          </div>
          <ReorderArrows
            onMoveUp={onMoveUp}
            onMoveDown={onMoveDown}
            isFirst={isFirst}
            isLast={isLast}
          />
          <div className="flex items-center gap-1 sm:opacity-0 sm:group-hover:opacity-100 transition-opacity">
            <ModuleStatusActions
              status={module.status ?? "DRAFT"}
              moduleId={module.id}
              courseId={courseId}
            />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-7 text-muted-foreground hover:text-foreground"
                  aria-label="Добавить"
                >
                  <Plus size={14} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={onAddMaterial}>
                  <FileText size={14} />
                  Добавить материал
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onAddQuiz}>
                  <Icons.quiz size={14} />
                  Добавить тест
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onAttachIssue} disabled={isAttachIssuePending}>
                  <ENTITY_ICONS.issue size={14} />
                  {isAttachIssuePending ? "Добавление..." : "Добавить задачу"}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-7 text-muted-foreground hover:text-foreground"
                  aria-label="Действия"
                >
                  <MoreHorizontal size={14} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {module.status === "PUBLISHED" && (
                  <DropdownMenuItem
                    disabled={isRestorePending}
                    onClick={() => restoreModule(module.id)}
                  >
                    <ArchiveRestore size={14} />В черновик
                  </DropdownMenuItem>
                )}
                {module.status === "PUBLISHED" && (
                  <DropdownMenuItem
                    disabled={isArchivePending}
                    onClick={() => archiveModule(module.id)}
                  >
                    <Archive size={14} />
                    Архивировать
                  </DropdownMenuItem>
                )}
                {module.status === "ARCHIVED" && (
                  <DropdownMenuItem
                    disabled={isRestorePending}
                    onClick={() => restoreModule(module.id)}
                  >
                    <ArchiveRestore size={14} />В черновик
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem
                  onClick={() =>
                    onEdit({
                      title: module.title ?? "",
                      description,
                      detailedDescription,
                    })
                  }
                >
                  <Pencil size={14} />
                  Редактировать
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem
                  className="text-red"
                  disabled={isDetachPending}
                  onClick={onDetach}
                >
                  <Trash2 size={14} />
                  Удалить
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>

        {/* Items list (materials + issues) + drop zone for cross-module drag */}
        <div
          ref={dropZoneRef}
          className={cn(
            "border-t px-3 transition-colors",
            items.length > 0 ? "py-2" : "py-0",
            isDropTarget && "bg-accent/40",
          )}
        >
          {items.length > 0 ? (
            <div className="space-y-0.5">
              {items.map((item, idx) => (
                <SortableModuleItemCard
                  key={item.referenceId}
                  item={item}
                  index={idx}
                  globalIndex={globalItemOffset + idx + 1}
                  moduleId={module.id}
                  isFirst={idx === 0}
                  isLast={idx === items.length - 1}
                  onMoveUp={() => onMoveItem(item.referenceId, idx, idx - 1)}
                  onMoveDown={() => onMoveItem(item.referenceId, idx, idx + 1)}
                  onEdit={() => {
                    if (item.itemType === "Material")
                      router.push(
                        routes.authorMaterialEdit(item.referenceId, { courseId, courseSlug }),
                      );
                    else if (item.itemType === "Quiz")
                      router.push(routes.authorQuizEdit(item.referenceId));
                    else if (item.itemType === "Issue") setEditIssueId(item.referenceId);
                  }}
                  onDelete={() => detachModuleItem.detachItem(item.referenceId)}
                  onViewPriorityChange={(viewPriority) =>
                    updateViewPriority({ referenceId: item.referenceId, viewPriority })
                  }
                />
              ))}
            </div>
          ) : (
            <div
              className={cn(
                "text-xs text-muted-foreground/50 text-center transition-all",
                isDropTarget ? "py-6" : "py-3",
              )}
            >
              {isDropTarget ? "Отпустите, чтобы переместить сюда" : "Пусто"}
            </div>
          )}
        </div>
      </Card>

      {editIssueId && (
        <EditIssueSheet
          open={!!editIssueId}
          onOpenChange={(open) => {
            if (!open) setEditIssueId(null);
          }}
          issueId={editIssueId}
          projectId=""
          projectName={module.title ?? ""}
          courseId={courseId}
        />
      )}
    </>
  );
}
