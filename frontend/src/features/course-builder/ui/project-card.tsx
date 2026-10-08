"use client";

import type { BuilderSectionDto } from "@/entities/course";
import type { ProjectItemDto } from "@/entities/project";
import { projectDetailQueryOptions } from "@/entities/project";
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
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable, useSortable } from "@dnd-kit/react/sortable";
import { useQuery } from "@tanstack/react-query";
import {
  Archive,
  ArchiveRestore,
  GripVertical,
  MoreHorizontal,
  Pencil,
  Plus,
  Trash2,
} from "lucide-react";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { useState } from "react";
import { useArchiveProject } from "../model/use-archive-project";
import { useDetachProjectIssue } from "../model/use-detach-project-issue";
import { useMoveProjectIssue } from "../model/use-move-project-issue";
import { useRestoreProject } from "../model/use-restore-project";
import { EditIssueSheet } from "./edit-issue-sheet";
import { ProjectStatusActions } from "./project-status-actions";
import { ReorderArrows } from "./reorder-arrows";
import { SortableIssueCard } from "./sortable-issue-card";
import { ProjectReviewContextDialog } from "./project-review-context-dialog";
import { Icons } from "@/shared/ui/icons";

interface ProjectCardProps {
  project: BuilderSectionDto;
  index: number;
  courseId: string;
  onEdit: () => void;
  onDetach: () => void;
  isDetachPending: boolean;
  onCreateIssue: () => void;
  /** Touch reorder fallback for the project itself (recipe 7). */
  isFirst: boolean;
  isLast: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

export function ProjectCard({
  project,
  index,
  courseId,
  onEdit,
  onDetach,
  isDetachPending,
  onCreateIssue,
  isFirst,
  isLast,
  onMoveUp,
  onMoveDown,
}: ProjectCardProps) {
  const { ref, isDragging } = useSortable({
    id: project.id,
    index,
  });

  const [editIssueId, setEditIssueId] = useState<string | null>(null);
  const [reviewContextOpen, setReviewContextOpen] = useState(false);

  const { data: projectDetail } = useQuery(projectDetailQueryOptions(project.id));
  const moveProjectIssue = useMoveProjectIssue(project.id);
  const detachProjectIssue = useDetachProjectIssue(project.id);
  const { archiveProject, isPending: isArchivePending } = useArchiveProject(courseId);
  const { restoreProject, isPending: isRestorePending } = useRestoreProject(courseId);

  const issues = (projectDetail?.items ?? [])
    .filter((item, idx, arr) => arr.findIndex((i) => i.issueId === item.issueId) === idx)
    .sort((a, b) => compareSortKey(a.sortKey, b.sortKey));

  const handleMoveIssue = async (
    items: ProjectItemDto[],
    issueId: string,
    initialIndex: number,
    newIndex: number,
  ) => {
    const reordered = [...items];
    const [removed] = reordered.splice(initialIndex, 1);
    reordered.splice(newIndex, 0, removed);

    const afterSortKey = newIndex > 0 ? reordered[newIndex - 1].sortKey : undefined;
    const beforeSortKey =
      newIndex < reordered.length - 1 ? reordered[newIndex + 1].sortKey : undefined;

    await moveProjectIssue.moveIssue({
      issueId,
      request: { afterSortKey, beforeSortKey },
    });
  };

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
                {project.title ?? "Без названия"}
              </span>
              <span className="hidden sm:inline-flex shrink-0">
                {project.status && <StatusBadge status={project.status as StatusType} />}
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
            <ProjectStatusActions
              status={project.status ?? "DRAFT"}
              projectId={project.id}
              courseId={courseId}
            />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-7 text-muted-foreground hover:text-foreground"
                >
                  <Plus size={14} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={onCreateIssue}>
                  <ENTITY_ICONS.issue size={14} />
                  Добавить задачу
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-7 text-muted-foreground hover:text-foreground"
                >
                  <MoreHorizontal size={14} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                {project.status === "PUBLISHED" && (
                  <DropdownMenuItem
                    disabled={isRestorePending}
                    onClick={() => restoreProject(project.id)}
                  >
                    <ArchiveRestore size={14} />В черновик
                  </DropdownMenuItem>
                )}
                {project.status === "PUBLISHED" && (
                  <DropdownMenuItem
                    disabled={isArchivePending}
                    onClick={() => archiveProject(project.id)}
                  >
                    <Archive size={14} />
                    Архивировать
                  </DropdownMenuItem>
                )}
                {project.status === "ARCHIVED" && (
                  <DropdownMenuItem
                    disabled={isRestorePending}
                    onClick={() => restoreProject(project.id)}
                  >
                    <ArchiveRestore size={14} />В черновик
                  </DropdownMenuItem>
                )}
                <DropdownMenuItem onClick={onEdit}>
                  <Pencil size={14} />
                  Редактировать
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => setReviewContextOpen(true)}>
                  <Icons.ai size={14} />
                  AI-проверка
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

        {/* Issues list */}
        {issues.length > 0 && (
          <div className="border-t px-3 py-2">
            <DragDropProvider
              onDragEnd={(event) => {
                if (event.canceled) return;
                const { source } = event.operation;
                if (!isSortable(source)) return;
                const { initialIndex, index: newIndex } = source;
                if (initialIndex === newIndex) return;
                handleMoveIssue(issues, source.id as string, initialIndex, newIndex);
              }}
            >
              <div className="space-y-0.5">
                {issues.map((item, idx) => (
                  <SortableIssueCard
                    key={item.issueId}
                    item={item}
                    index={idx}
                    isFirst={idx === 0}
                    isLast={idx === issues.length - 1}
                    onMoveUp={() => handleMoveIssue(issues, item.issueId, idx, idx - 1)}
                    onMoveDown={() => handleMoveIssue(issues, item.issueId, idx, idx + 1)}
                    onEdit={() => setEditIssueId(item.issueId)}
                    onDelete={() => detachProjectIssue.detachIssue(item.issueId)}
                  />
                ))}
              </div>
            </DragDropProvider>
          </div>
        )}
      </Card>

      {editIssueId && (
        <EditIssueSheet
          open={!!editIssueId}
          onOpenChange={(open) => {
            if (!open) setEditIssueId(null);
          }}
          issueId={editIssueId}
          projectId={project.id}
          projectName={project.title ?? ""}
          courseId={courseId}
        />
      )}

      <ProjectReviewContextDialog
        open={reviewContextOpen}
        onOpenChange={setReviewContextOpen}
        projectId={project.id}
        projectName={project.title ?? ""}
      />
    </>
  );
}
