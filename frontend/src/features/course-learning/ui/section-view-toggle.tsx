"use client";

import { cn } from "@/shared/lib/css";
import { ENTITY_ICONS } from "@/shared/config/entity-icons";

export type SectionViewMode = "modules" | "projects";
export type ExtendedSectionViewMode = SectionViewMode | "articles";

interface SectionViewToggleProps {
  value: ExtendedSectionViewMode;
  onChange: (value: ExtendedSectionViewMode) => void;
  className?: string;
  showArticles?: boolean;
  labels?: {
    modules?: string;
    projects?: string;
    articles?: string;
  };
}

export function SectionViewToggle({
  value,
  onChange,
  className,
  showArticles = false,
  labels,
}: SectionViewToggleProps) {
  const modulesLabel = labels?.modules ?? "Модули";
  const projectsLabel = labels?.projects ?? "Проекты";
  const articlesLabel = labels?.articles ?? "Статьи";
  return (
    <div
      className={cn(
        "inline-flex gap-1 p-1 rounded-lg bg-muted w-full",
        className,
      )}
    >
      <button
        type="button"
        onClick={() => onChange("modules")}
        className={cn(
          "flex-1 min-w-0 inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors whitespace-nowrap",
          value === "modules"
            ? "bg-primary text-primary-foreground shadow-sm"
            : "text-muted-foreground hover:text-foreground",
        )}
      >
        <ENTITY_ICONS.module size={12} />
        {modulesLabel}
      </button>
      <button
        type="button"
        onClick={() => onChange("projects")}
        className={cn(
          "flex-1 min-w-0 inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors whitespace-nowrap",
          value === "projects"
            ? "bg-primary text-primary-foreground shadow-sm"
            : "text-muted-foreground hover:text-foreground",
        )}
      >
        <ENTITY_ICONS.project size={12} />
        {projectsLabel}
      </button>
      {showArticles && (
        <button
          type="button"
          onClick={() => onChange("articles")}
          className={cn(
            "flex-1 min-w-0 inline-flex items-center justify-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors whitespace-nowrap",
            value === "articles"
              ? "bg-primary text-primary-foreground shadow-sm"
              : "text-muted-foreground hover:text-foreground",
          )}
        >
          {articlesLabel}
        </button>
      )}
    </div>
  );
}
