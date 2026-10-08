"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { projectDetailQueryOptions } from "@/entities/project";
import type { BuilderSectionDto } from "@/entities/course";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Search } from "lucide-react";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";
import { cn } from "@/shared/lib/css";
import type { StatusType } from "@/shared/ui/components/status-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";

interface IssuePickerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  courseItems: BuilderSectionDto[];
  onSelect: (issueId: string) => void;
  isPending: boolean;
}

export function IssuePickerDialog({
  open,
  onOpenChange,
  courseItems,
  onSelect,
  isPending,
}: IssuePickerDialogProps) {
  const [search, setSearch] = useState("");

  const projectItems = courseItems.filter((item) => item.itemType === "Project");

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[80vh] flex-col overflow-hidden sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Добавить задачу в модуль</DialogTitle>
          <DialogDescription>
            Выберите задачу из проектов курса
          </DialogDescription>
        </DialogHeader>

        <div className="relative">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
          <Input
            placeholder="Поиск задач..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9 h-9"
          />
        </div>

        <div className="min-h-0 flex-1 space-y-3 overflow-y-auto pr-2 [scrollbar-gutter:stable]">
          {projectItems.map((project) => (
            <ProjectIssuesList
              key={project.id}
              projectId={project.id}
              projectTitle={project.title ?? "Проект"}
              search={search}
              onSelect={onSelect}
              isPending={isPending}
            />
          ))}
          {projectItems.length === 0 && (
            <p className="text-sm text-muted-foreground text-center py-4">
              В курсе нет проектов с задачами
            </p>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function ProjectIssuesList({
  projectId,
  projectTitle,
  search,
  onSelect,
  isPending,
}: {
  projectId: string;
  projectTitle: string;
  search: string;
  onSelect: (issueId: string) => void;
  isPending: boolean;
}) {
  const { data: project } = useQuery(projectDetailQueryOptions(projectId));

  const filteredIssues = (project?.items ?? []).filter((item) =>
    !search || (item.title ?? "").toLowerCase().includes(search.toLowerCase()),
  );

  if (filteredIssues.length === 0) return null;

  return (
    <div>
      <p className="text-xs font-medium text-muted-foreground px-1 mb-1">
        {projectTitle}
      </p>
      <div className="space-y-0.5">
        {filteredIssues.map((item) => (
          <button
            key={item.issueId}
            type="button"
            disabled={isPending}
            onClick={() => onSelect(item.issueId)}
            className={cn(
              "w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm text-left",
              "hover:bg-accent transition-colors",
              isPending && "opacity-50 cursor-not-allowed",
            )}
          >
            <ENTITY_ICONS.issue size={13} className="text-orange shrink-0" />
            <span className="truncate flex-1">{item.title ?? "Без названия"}</span>
            {item.status && (
              <StatusBadge status={item.status as StatusType} />
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
