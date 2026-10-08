"use client";

import {
  courseMaterialIdsQueryOptions,
  courseMaterialsInfiniteOptions,
  getMaterialAccessBadge,
  getMaterialKindBadge,
  getMaterialStatusBadge,
  MATERIAL_KINDS_WITH_ALL,
  MaterialPickerDialog,
  type MaterialKind,
  type MaterialSummaryDto,
} from "@/entities/material";
import { routes } from "@/shared/config/routes";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { useDebouncedValue } from "@/shared/hooks/use-debounced-value";
import { cn } from "@/shared/lib/css";
import { stripMarkdown } from "@/shared/lib/strip-markdown";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useAttachMaterialToCourse } from "../model/use-attach-material-to-course";
import { useDetachMaterialFromCourse } from "../model/use-detach-material-from-course";

interface CourseBuilderMaterialsTabProps {
  courseId: string;
}

export function CourseBuilderMaterialsTab({ courseId }: CourseBuilderMaterialsTabProps) {
  const courseSlug = useCourseSlug();
  const [pickerOpen, setPickerOpen] = useState(false);
  const [kindFilter, setKindFilter] = useState<MaterialKind | "all">("all");
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput, 300);
  const kind = kindFilter === "all" ? undefined : kindFilter;

  const { data, isLoading, error, hasNextPage, isFetchingNextPage, fetchNextPage } =
    useInfiniteQuery(
      courseMaterialsInfiniteOptions(courseId, { kind, search: search || undefined }),
    );

  // Полный список id прикреплённых материалов (без pagination) — для корректного
  // фильтра в MaterialPickerDialog. `data.items` — только подгруженные страницы.
  const { data: attachedIds } = useQuery(courseMaterialIdsQueryOptions(courseId));

  const { attachMaterial, isPending: isAttaching } = useAttachMaterialToCourse(courseId);
  const { detachMaterial } = useDetachMaterialFromCourse(courseId);

  // Отдельный per-item флаг: какую именно карточку сейчас открепляем.
  // Без этого useDetachMaterialFromCourse's isPending блокировал бы все
  // Unlink-кнопки сразу и крутил бы спиннеры во всех строках одновременно.
  const [detachingId, setDetachingId] = useState<string | null>(null);

  const items = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const existingIds = new Set(attachedIds ?? []);

  const sentinelRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = sentinelRef.current;
    if (!el) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting && hasNextPage && !isFetchingNextPage) {
          void fetchNextPage();
        }
      },
      { rootMargin: "400px" },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, [fetchNextPage, hasNextPage, isFetchingNextPage]);

  const handleAttach = async (materialId: string) => {
    await attachMaterial(materialId);
    setPickerOpen(false);
  };

  if (isLoading) {
    return (
      <div className="flex justify-center py-16">
        <Icons.loading className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return <ErrorCard error={error} className="py-16" />;
  }

  const hasAnyContent = totalCount > 0 || items.length > 0;
  const isFiltered = kindFilter !== "all" || search.length > 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h2 className="text-base font-semibold">Материалы курса</h2>
          <p className="text-sm text-muted-foreground mt-0.5">
            Все материалы курса — из модулей, подборок и прикреплённые напрямую
            {totalCount > 0 && <span className="ml-1.5">· {totalCount}</span>}
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <Button variant="outline" size="sm" onClick={() => setPickerOpen(true)}>
            <Icons.add size={14} /> Добавить
          </Button>
          <Button
            size="sm"
            className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90"
            asChild
          >
            <Link href={routes.authorKnowledgeBaseNew({ courseId, courseSlug })}>
              <Icons.add size={14} /> Создать
            </Link>
          </Button>
        </div>
      </div>

      {hasAnyContent && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative min-w-0 flex-1">
            <Icons.search
              size={14}
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Поиск по названию..."
              className="pl-9"
            />
          </div>
          <div className="flex flex-wrap items-center gap-1.5 overflow-x-auto">
            {MATERIAL_KINDS_WITH_ALL.map((t) => (
              <KindChip
                key={t.value}
                active={kindFilter === t.value}
                onClick={() => setKindFilter(t.value)}
              >
                {t.label}
              </KindChip>
            ))}
          </div>
        </div>
      )}

      {!hasAnyContent && (
        <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
          <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
            <Icons.library size={22} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium mb-1">Нет материалов</p>
          <p className="text-sm text-muted-foreground mb-4">
            Материалы появятся здесь, когда вы добавите их в модули, подборки или прикрепите к курсу
          </p>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => setPickerOpen(true)}>
              <Icons.add size={14} /> Добавить существующий
            </Button>
          </div>
        </div>
      )}

      {hasAnyContent && items.length === 0 && isFiltered && (
        <div className="rounded-xl border border-dashed border-border/60 bg-card/40 py-12 text-center text-sm text-muted-foreground">
          Ничего не найдено по фильтру
        </div>
      )}

      {items.length > 0 && (
        <div className="space-y-2">
          {items.map((material) => (
            <CourseMaterialCard
              key={material.id}
              material={material}
              courseId={courseId}
              courseSlug={courseSlug}
              onDetach={async () => {
                setDetachingId(material.id);
                try {
                  await detachMaterial(material.id);
                } finally {
                  setDetachingId(null);
                }
              }}
              isDetaching={detachingId === material.id}
            />
          ))}

          {hasNextPage && (
            <div
              ref={sentinelRef}
              className="flex items-center justify-center py-6 text-xs text-muted-foreground"
            >
              {isFetchingNextPage ? (
                <span className="inline-flex items-center gap-2">
                  <Icons.loading size={14} className="animate-spin" />
                  Загружаем ещё…
                </span>
              ) : (
                <span>Прокрутите, чтобы загрузить больше</span>
              )}
            </div>
          )}
        </div>
      )}

      <MaterialPickerDialog
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        title="Добавить материал к курсу"
        description="Выберите материал из библиотеки для привязки к курсу"
        courseId={courseId}
        onSelect={(id) => void handleAttach(id)}
        isPending={isAttaching}
        excludedIds={existingIds}
      />
    </div>
  );
}

function KindChip({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "px-3 py-1.5 rounded-full text-xs font-medium border transition-colors whitespace-nowrap",
        active
          ? "bg-primary text-primary-foreground border-primary"
          : "bg-card text-muted-foreground border-border/60 hover:text-foreground hover:border-border",
      )}
    >
      {children}
    </button>
  );
}

function CourseMaterialCard({
  material,
  courseId,
  courseSlug,
  onDetach,
  isDetaching,
}: {
  material: MaterialSummaryDto;
  courseId: string;
  courseSlug: string;
  onDetach: () => void | Promise<void>;
  isDetaching: boolean;
}) {
  const kindBadge = getMaterialKindBadge(material.kind);
  const statusBadge = getMaterialStatusBadge(material.status);
  const accessBadge = getMaterialAccessBadge(material.accessType);

  return (
    <div className="group flex items-start gap-3 rounded-xl border border-border/60 bg-card p-3 transition-colors hover:bg-accent/20">
      <div
        className={cn(
          "flex size-9 shrink-0 items-center justify-center rounded-lg mt-0.5",
          kindBadge.iconBgClassName,
        )}
      >
        <kindBadge.icon className="size-4" />
      </div>

      <div className="min-w-0 flex-1 space-y-1">
        <Link
          href={routes.authorMaterialEdit(material.id, { courseId, courseSlug })}
          prefetch={false}
          className="text-sm font-medium hover:text-primary transition-colors line-clamp-1 block"
        >
          {material.title}
        </Link>
        {material.preview && (
          <p className="text-xs text-muted-foreground line-clamp-1">
            {stripMarkdown(material.preview)}
          </p>
        )}
        <div className="flex flex-wrap items-center gap-1.5 pt-0.5">
          <Badge variant="outline" className={cn("text-xs", kindBadge.className)}>
            {kindBadge.label}
          </Badge>
          <Badge variant="outline" className={cn("text-xs", accessBadge.className)}>
            {accessBadge.label}
          </Badge>
          <Badge variant="outline" className={cn("text-xs", statusBadge.className)}>
            {statusBadge.label}
          </Badge>
        </div>
      </div>

      <Button
        size="sm"
        variant="ghost"
        className="h-8 w-8 p-0 sm:opacity-0 sm:group-hover:opacity-100 text-destructive hover:text-destructive shrink-0"
        onClick={() => void onDetach()}
        disabled={isDetaching}
        title="Открепить от курса"
      >
        {isDetaching ? (
          <Icons.loading size={14} className="animate-spin" />
        ) : (
          <Icons.unlink size={14} />
        )}
      </Button>
    </div>
  );
}
