"use client";

import type { BuilderSectionDto, CourseSummaryDto } from "@/entities/course";
import { courseBuilderQueryOptions, coursesApi, coursesQueryOptions } from "@/entities/course";
import { projectDetailQueryOptions } from "@/entities/project";
import type { EntityReferenceData, EntityReferenceType } from "@/entities/roadmap";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { queryOptions, useQuery } from "@tanstack/react-query";
import { ArrowLeft, ChevronRight, Search } from "lucide-react";
import {
  ENTITY_ICONS as CENTRAL_ICONS,
  ENTITY_COLORS as CENTRAL_COLORS,
} from "@/shared/config/entity-icons";
import { useState } from "react";

interface EntityPickerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSelect: (data: EntityReferenceData) => void;
}

type Step = "course-list" | "course-tree";

/** Map PascalCase entity types to central config keys */
const TYPE_KEY: Record<string, keyof typeof CENTRAL_ICONS> = {
  Course: "course",
  Module: "module",
  Material: "lesson",
  Project: "project",
  Issue: "issue",
};

function getPickerIcon(entityType: string) {
  const key = TYPE_KEY[entityType];
  return key ? CENTRAL_ICONS[key] : CENTRAL_ICONS.lesson;
}

function getPickerColor(entityType: string) {
  const key = TYPE_KEY[entityType];
  return key ? CENTRAL_COLORS[key] : "text-muted-foreground";
}

export function EntityPickerDialog({ open, onOpenChange, onSelect }: EntityPickerDialogProps) {
  const [step, setStep] = useState<Step>("course-list");
  const [selectedCourseId, setSelectedCourseId] = useState<string | null>(null);
  const [selectedCourseTitle, setSelectedCourseTitle] = useState("");
  const [search, setSearch] = useState("");

  function handleSelectCourse(course: CourseSummaryDto) {
    setSelectedCourseId(course.id);
    setSelectedCourseTitle(course.title);
    setStep("course-tree");
    setSearch("");
  }

  function handleSelectAsCourseNode(course: CourseSummaryDto) {
    onSelect({
      entityType: "Course",
      entityId: course.id,
      entityTitle: course.title,
      entityDescription: course.description,
      entityStatus: course.status,
      imageId: course.imageId,
    });
    resetAndClose();
  }

  function handleSelectEntity(
    entityType: EntityReferenceType,
    entityId: string,
    title: string,
    description?: string | null,
    status?: string | null,
  ) {
    onSelect({
      entityType,
      entityId,
      courseId: selectedCourseId ?? undefined,
      entityTitle: title,
      entityDescription: description,
      entityStatus: status,
    });
    resetAndClose();
  }

  function resetAndClose() {
    onOpenChange(false);
    setStep("course-list");
    setSelectedCourseId(null);
    setSearch("");
  }

  function handleBack() {
    setStep("course-list");
    setSelectedCourseId(null);
    setSearch("");
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="w-full max-w-[calc(100%-2rem)] sm:!max-w-[600px] lg:!max-w-[750px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            {step === "course-tree" && (
              <Button variant="ghost" size="icon" className="size-7" onClick={handleBack}>
                <ArrowLeft className="size-4" />
              </Button>
            )}
            {step === "course-list" ? "Выберите курс" : `${selectedCourseTitle}`}
          </DialogTitle>
          <DialogDescription>
            {step === "course-list"
              ? "Выберите курс или добавьте его как элемент роадмапа"
              : "Выберите элемент из структуры курса"}
          </DialogDescription>
        </DialogHeader>

        <div className="relative">
          <Search
            size={14}
            className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
          />
          <Input
            placeholder="Поиск..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="h-9 pl-9"
          />
        </div>

        <div className="max-h-[420px] overflow-y-auto pr-1 scrollbar-thin scrollbar-track-transparent scrollbar-thumb-border">
          {step === "course-list" && (
            <CourseList
              search={search}
              onSelectCourse={handleSelectCourse}
              onSelectAsCourseNode={handleSelectAsCourseNode}
            />
          )}
          {step === "course-tree" && selectedCourseId && (
            <CourseTreePicker
              courseId={selectedCourseId}
              search={search}
              onSelect={handleSelectEntity}
            />
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CourseList({
  search,
  onSelectCourse,
  onSelectAsCourseNode,
}: {
  search: string;
  onSelectCourse: (course: CourseSummaryDto) => void;
  onSelectAsCourseNode: (course: CourseSummaryDto) => void;
}) {
  const { data: courses } = useQuery(
    queryOptions({
      queryKey: [coursesQueryOptions.baseKey, "my", "picker"],
      queryFn: ({ signal }) => coursesApi.getMyCourses({ limit: 50 }, { signal }),
      select: (data) => data.result?.items ?? [],
    }),
  );

  const filtered = (courses ?? []).filter(
    (c) => !search || c.title.toLowerCase().includes(search.toLowerCase()),
  );

  if (filtered.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-muted-foreground">
        {search ? "Ничего не найдено" : "Нет доступных курсов"}
      </p>
    );
  }

  return (
    <div className="space-y-1">
      {filtered.map((course) => (
        <div
          key={course.id}
          className="group flex items-center gap-3 rounded-lg px-3 py-2.5 transition-colors hover:bg-accent"
        >
          <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-purple-500/10">
            <CENTRAL_ICONS.course className="size-4 text-purple" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{course.title}</p>
            <p className="truncate text-2xs text-muted-foreground">{course.description}</p>
          </div>
          <div className="flex shrink-0 gap-1 opacity-0 transition-opacity group-hover:opacity-100">
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs"
              onClick={() => onSelectAsCourseNode(course)}
            >
              Как курс
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs"
              onClick={() => onSelectCourse(course)}
            >
              Внутрь
            </Button>
          </div>
        </div>
      ))}
    </div>
  );
}

function CourseTreePicker({
  courseId,
  search,
  onSelect,
}: {
  courseId: string;
  search: string;
  onSelect: (
    type: EntityReferenceType,
    id: string,
    title: string,
    desc?: string | null,
    status?: string | null,
  ) => void;
}) {
  const { data: builder, isLoading } = useQuery(courseBuilderQueryOptions(courseId));
  const [tab, setTab] = useState<"modules" | "projects">("modules");

  if (isLoading) {
    return <p className="py-6 text-center text-sm text-muted-foreground">Загрузка...</p>;
  }

  if (!builder) {
    return <p className="py-6 text-center text-sm text-muted-foreground">Не удалось загрузить</p>;
  }

  const sections = builder.sections ?? [];
  const lowerSearch = search.toLowerCase();
  const modules = sections.filter((s) => s.itemType === "Module");
  const projects = sections.filter((s) => s.itemType === "Project");
  const visibleSections = tab === "modules" ? modules : projects;

  return (
    <div className="space-y-2">
      {/* Tabs */}
      {modules.length > 0 && projects.length > 0 && (
        <div className="flex gap-1 rounded-lg bg-muted/50 p-0.5">
          <button
            type="button"
            onClick={() => setTab("modules")}
            className={cn(
              "flex-1 rounded-md px-3 py-1.5 text-xs font-medium transition-colors",
              tab === "modules"
                ? "bg-card text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            Модули ({modules.length})
          </button>
          <button
            type="button"
            onClick={() => setTab("projects")}
            className={cn(
              "flex-1 rounded-md px-3 py-1.5 text-xs font-medium transition-colors",
              tab === "projects"
                ? "bg-card text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            Проекты ({projects.length})
          </button>
        </div>
      )}

      {visibleSections.map((section) => (
        <SectionGroup key={section.id} section={section} search={lowerSearch} onSelect={onSelect} />
      ))}
      {visibleSections.length === 0 && (
        <p className="py-6 text-center text-sm text-muted-foreground">
          {tab === "modules" ? "Нет модулей" : "Нет проектов"}
        </p>
      )}
    </div>
  );
}

function SectionGroup({
  section,
  search,
  onSelect,
}: {
  section: BuilderSectionDto;
  search: string;
  onSelect: (
    type: EntityReferenceType,
    id: string,
    title: string,
    desc?: string | null,
    status?: string | null,
  ) => void;
}) {
  const isModule = section.itemType === "Module";
  const isProject = section.itemType === "Project";
  const sectionType: EntityReferenceType = isModule ? "Module" : "Project";
  const SectionIcon = isModule ? CENTRAL_ICONS.module : CENTRAL_ICONS.project;
  const sectionColor = isModule ? CENTRAL_COLORS.module : CENTRAL_COLORS.project;
  const sectionTitle = section.title ?? "Без названия";

  const sectionMatches = sectionTitle.toLowerCase().includes(search);
  const [expanded, setExpanded] = useState(!!search);

  const filteredItems = (section.items ?? []).filter(
    (item) => !search || sectionMatches || (item.title ?? "").toLowerCase().includes(search),
  );

  if (!sectionMatches && filteredItems.length === 0 && search) return null;

  return (
    <div>
      {/* Section header: chevron expands, click title selects */}
      <div className="flex items-center rounded-md hover:bg-accent/30">
        <button
          type="button"
          onClick={() => setExpanded((v) => !v)}
          className="flex shrink-0 items-center justify-center p-2"
          title={expanded ? "Свернуть" : "Развернуть"}
        >
          <ChevronRight
            className={cn(
              "size-4 text-muted-foreground transition-transform",
              expanded && "rotate-90",
            )}
          />
        </button>
        <button
          type="button"
          onClick={() =>
            onSelect(sectionType, section.id, sectionTitle, section.description, section.status)
          }
          className="flex min-w-0 flex-1 items-center gap-2 py-2 pr-3 text-left"
          title="Добавить на роадмап"
        >
          <SectionIcon className={cn("size-4 shrink-0", sectionColor)} />
          <span className="min-w-0 flex-1 truncate text-sm font-medium">{sectionTitle}</span>
        </button>
      </div>

      {/* Children — click selects */}
      {expanded && isModule && (
        <div className="ml-7 space-y-px border-l border-border/50 pl-3">
          {filteredItems.map((item) => {
            const itemType: EntityReferenceType =
              item.itemType === "Material"
                ? "Material"
                : item.itemType === "Issue"
                  ? "Issue"
                  : "Quiz";
            const ItemIcon = getPickerIcon(itemType);
            const itemColor = getPickerColor(itemType);

            return (
              <button
                key={`${section.id}-${item.id}`}
                type="button"
                onClick={() =>
                  onSelect(
                    itemType,
                    item.referenceId,
                    item.title ?? "Без названия",
                    null,
                    item.status,
                  )
                }
                className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-accent"
              >
                <ItemIcon className={cn("size-3.5 shrink-0", itemColor)} />
                <span className="min-w-0 flex-1 text-sm">{item.title ?? "Без названия"}</span>
              </button>
            );
          })}
        </div>
      )}

      {expanded && isProject && (
        <ProjectIssues
          projectId={section.id}
          search={search}
          sectionMatches={sectionMatches}
          onSelect={onSelect}
        />
      )}
    </div>
  );
}

function ProjectIssues({
  projectId,
  search,
  sectionMatches,
  onSelect,
}: {
  projectId: string;
  search: string;
  sectionMatches: boolean;
  onSelect: (
    type: EntityReferenceType,
    id: string,
    title: string,
    desc?: string | null,
    status?: string | null,
  ) => void;
}) {
  const { data: project } = useQuery(projectDetailQueryOptions(projectId));

  const filteredIssues = (project?.items ?? []).filter(
    (item) => !search || sectionMatches || (item.title ?? "").toLowerCase().includes(search),
  );

  if (filteredIssues.length === 0) return null;

  return (
    <div className="ml-7 space-y-px border-l border-border/50 pl-3">
      {filteredIssues.map((item) => (
        <button
          key={`${projectId}-${item.issueId}`}
          type="button"
          onClick={() =>
            onSelect("Issue", item.issueId, item.title ?? "Без названия", null, item.status)
          }
          className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left transition-colors hover:bg-accent"
        >
          <CENTRAL_ICONS.issue className={cn("size-3.5 shrink-0", CENTRAL_COLORS.issue)} />
          <span className="min-w-0 flex-1 text-sm">{item.title ?? "Без названия"}</span>
        </button>
      ))}
    </div>
  );
}
