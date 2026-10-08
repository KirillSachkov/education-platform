"use client";

import type { ProjectItemDto } from "@/entities/project";
import { cn } from "@/shared/lib/css";
import type { StatusType } from "@/shared/ui/components/status-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useSortable } from "@dnd-kit/react/sortable";
import { ReorderArrows } from "./reorder-arrows";

interface SortableIssueCardProps {
  item: ProjectItemDto;
  index: number;
  isFirst: boolean;
  isLast: boolean;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onEdit: () => void;
  onDelete: () => void;
}

export function SortableIssueCard({
  item,
  index,
  isFirst,
  isLast,
  onMoveUp,
  onMoveDown,
  onEdit,
  onDelete,
}: SortableIssueCardProps) {
  const { ref, isDragging } = useSortable({
    id: item.issueId,
    index,
  });

  return (
    <div
      ref={ref}
      className={cn(
        "group/issue flex items-center gap-2 sm:gap-3 px-2 sm:px-3 py-2.5 rounded-lg hover:bg-accent/30 transition-colors",
        isDragging && "opacity-50",
      )}
    >
      <Icons.drag size={14} className="text-muted-foreground/40 cursor-grab shrink-0" />

      <ReorderArrows
        onMoveUp={onMoveUp}
        onMoveDown={onMoveDown}
        isFirst={isFirst}
        isLast={isLast}
        compact
      />

      <span className="text-[10px] tabular-nums text-muted-foreground/40 w-4 text-right shrink-0">
        {String(index + 1).padStart(2, "0")}
      </span>
      <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-orange-500/15">
        <Icons.issue size={12} className="text-orange" />
      </span>
      <button
        type="button"
        onClick={onEdit}
        className="text-sm truncate flex-1 hover:underline text-left bg-transparent p-0 cursor-pointer"
      >
        {item.title ?? "Без названия"}
      </button>
      <span className="hidden md:inline-flex rounded border px-1.5 py-0.5 text-[10px] text-muted-foreground shrink-0">
        {item.submissionMode === "SELF_CHECK" ? "Самопроверка" : "PR"}
      </span>
      <span className="hidden sm:inline-flex shrink-0">
        {item.status && <StatusBadge status={item.status as StatusType} />}
      </span>
      <div className="flex items-center gap-0.5 shrink-0 sm:invisible sm:group-hover/issue:visible">
        <Button
          variant="ghost"
          size="icon"
          className="size-7 text-muted-foreground hover:text-foreground"
          onClick={onEdit}
          aria-label="Редактировать"
        >
          <Icons.edit size={14} />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="size-7 text-muted-foreground hover:text-red"
          onClick={onDelete}
          aria-label="Удалить"
        >
          <Icons.delete size={14} />
        </Button>
      </div>
    </div>
  );
}
