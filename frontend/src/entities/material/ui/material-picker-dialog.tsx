"use client";

import {
  getMaterialAccessBadge,
  getMaterialKindBadge,
  getMaterialStatusBadge,
} from "../lib/material-ui";
import { mergeMaterialItems } from "../lib/merge-material-items";
import { materialsQueryOptions } from "../api";
import type { MaterialId, MaterialSummaryDto } from "../types";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { useCourseContext } from "@/shared/providers/course-id-provider";
import { useDebouncedValue } from "@/shared/hooks";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/shared/ui/kit/tooltip";
import { useInfiniteQuery } from "@tanstack/react-query";
import { stripMarkdown } from "@/shared/lib/strip-markdown";
import { BookMarked, FilePlus2, FileText, Loader2, Search } from "lucide-react";
import Link from "next/link";
import { useState } from "react";

interface MaterialPickerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  /** Course to associate a new material with. Omit for space-level usage (e.g. collections without a course). */
  courseId?: string;
  moduleId?: string;
  /**
   * When opened from a collection section, passing these props makes "Создать новый"
   * redirect back to the collection editor and auto-attach the new material to the section.
   */
  collectionId?: string;
  sectionId?: string;
  onSelect: (materialId: MaterialId) => void;
  isPending: boolean;
  excludedIds?: Set<string>;
}

export function MaterialPickerDialog({
  open,
  onOpenChange,
  title,
  description,
  courseId,
  moduleId,
  collectionId,
  sectionId,
  onSelect,
  isPending,
  excludedIds,
}: MaterialPickerDialogProps) {
  const courseContext = useCourseContext();
  const effectiveCourseId = courseId ?? courseContext?.courseId;
  const effectiveCourseSlug = courseContext?.courseSlug;
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search.trim(), 250);
  const serverSearch = debouncedSearch.length > 0 ? debouncedSearch : undefined;

  const mineQuery = useInfiniteQuery({
    ...materialsQueryOptions.getListInfiniteOptions({
      scope: "mine",
      limit: 50,
      search: serverSearch,
    }),
    enabled: open,
  });
  const publicQuery = useInfiniteQuery({
    ...materialsQueryOptions.getListInfiniteOptions({
      scope: "public",
      limit: 50,
      search: serverSearch,
    }),
    enabled: open,
  });

  const items = mergeMaterialItems(
    mineQuery.data?.items ?? [],
    publicQuery.data?.items ?? [],
  ).filter((material) => !excludedIds?.has(material.id));

  const isLoading = mineQuery.isLoading || publicQuery.isLoading;
  const hasMore = Boolean(mineQuery.hasNextPage || publicQuery.hasNextPage);
  const isLoadingMore = mineQuery.isFetchingNextPage || publicQuery.isFetchingNextPage;

  const handleLoadMore = async () => {
    await Promise.all([
      mineQuery.hasNextPage && !mineQuery.isFetchingNextPage
        ? mineQuery.fetchNextPage()
        : Promise.resolve(),
      publicQuery.hasNextPage && !publicQuery.isFetchingNextPage
        ? publicQuery.fetchNextPage()
        : Promise.resolve(),
    ]);
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        onOpenChange(nextOpen);
        if (!nextOpen) {
          setSearch("");
        }
      }}
    >
      <DialogContent className="flex max-h-[90vh] max-w-4xl flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative min-w-0 flex-1">
            <Search
              size={14}
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Поиск материала..."
              className="pl-9"
            />
          </div>

          <Button variant="outline" className="shrink-0" asChild>
            <Link
              href={routes.authorKnowledgeBaseNew({
                courseId: effectiveCourseId,
                courseSlug: effectiveCourseSlug,
                moduleId,
                collectionId,
                sectionId,
              })}
              onClick={() => onOpenChange(false)}
            >
              <FilePlus2 size={14} />
              Создать новый
            </Link>
          </Button>
        </div>

        <div className="max-h-[65vh] overflow-y-scroll pr-2 [scrollbar-gutter:stable]">
          <div className="space-y-2 pr-2">
            {isLoading ? (
              <div className="flex items-center justify-center py-10">
                <Loader2 className="size-5 animate-spin text-muted-foreground" />
              </div>
            ) : items.length === 0 ? (
              <EmptyPickerState
                hasSearch={debouncedSearch.length > 0}
                totalLoaded={
                  (mineQuery.data?.items.length ?? 0) + (publicQuery.data?.items.length ?? 0)
                }
                excludedCount={excludedIds?.size ?? 0}
              />
            ) : (
              items.map((material) => (
                <MaterialListItem
                  key={material.id}
                  material={material}
                  isPending={isPending}
                  onSelect={onSelect}
                />
              ))
            )}

            {hasMore && (
              <div className="flex justify-center pt-2">
                <Button
                  variant="outline"
                  onClick={() => void handleLoadMore()}
                  disabled={isLoadingMore}
                >
                  {isLoadingMore ? (
                    <>
                      <Loader2 size={14} className="animate-spin" />
                      Загружаем...
                    </>
                  ) : (
                    "Показать ещё"
                  )}
                </Button>
              </div>
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function EmptyPickerState({
  hasSearch,
  totalLoaded,
  excludedCount,
}: {
  hasSearch: boolean;
  totalLoaded: number;
  excludedCount: number;
}) {
  // Все загруженные материалы исключены → реально всё уже в курсе.
  // Иначе — либо нет совпадений по поиску, либо нет материалов вовсе.
  const allAttached = totalLoaded > 0 && excludedCount >= totalLoaded && !hasSearch;

  if (allAttached) {
    return (
      <div className="py-10 text-center text-sm text-muted-foreground space-y-1">
        <p className="font-medium text-foreground">Все ваши материалы уже в курсе</p>
        <p>Создайте новый материал кнопкой справа или открепите ненужные из списка курса.</p>
      </div>
    );
  }

  if (hasSearch) {
    return (
      <p className="py-10 text-center text-sm text-muted-foreground">Ничего не найдено по поиску</p>
    );
  }

  return (
    <p className="py-10 text-center text-sm text-muted-foreground">
      Подходящих материалов не найдено
    </p>
  );
}

function MaterialListItem({
  material,
  isPending,
  onSelect,
}: {
  material: MaterialSummaryDto;
  isPending: boolean;
  onSelect: (id: MaterialId) => void;
}) {
  const kindBadge = getMaterialKindBadge(material.kind);
  const statusBadge = getMaterialStatusBadge(material.status);
  const accessBadge = getMaterialAccessBadge(material.accessType);
  const courses = material.courses ?? [];

  return (
    <button
      type="button"
      disabled={isPending}
      onClick={() => onSelect(material.id)}
      className={cn(
        "w-full rounded-xl border border-border/70 bg-card/70 p-4 text-left transition-colors hover:border-primary/40 hover:bg-accent/20",
        isPending && "cursor-not-allowed opacity-60",
      )}
    >
      <div className="min-w-0 space-y-3">
        <div className="flex min-w-0 items-start gap-2">
          <FileText size={15} className="mt-0.5 shrink-0 text-primary" />
          <span className="line-clamp-2 break-words text-sm font-semibold [overflow-wrap:anywhere]">
            {material.title}
          </span>
        </div>
        <div className="flex flex-wrap gap-2">
          <Badge variant="outline" className={kindBadge.className}>
            {kindBadge.label}
          </Badge>
          <Badge variant="outline" className={accessBadge.className}>
            {accessBadge.label}
          </Badge>
          <Badge variant="outline" className={statusBadge.className}>
            {statusBadge.label}
          </Badge>
          <MaterialSourceBadge courses={courses} />
        </div>
        {material.preview && (
          <p className="line-clamp-2 break-words text-sm text-muted-foreground [overflow-wrap:anywhere]">
            {stripMarkdown(material.preview)}
          </p>
        )}
      </div>
    </button>
  );
}

/**
 * Бейдж источника материала: показывает в каком курсе он уже привязан, чтобы автор
 * видел переиспользование при выборе из picker'а. Для одного курса — название inline,
 * для нескольких — первое + «+N» с tooltip-списком. Если поле `courses` отсутствует
 * или массив пуст — материал не показываем (минимизируем визуальный шум).
 */
function MaterialSourceBadge({
  courses,
}: {
  courses: NonNullable<MaterialSummaryDto["courses"]>;
}) {
  if (courses.length === 0) return null;

  const [first, ...rest] = courses;

  if (rest.length === 0) {
    return (
      <Badge
        variant="outline"
        className="border-muted/60 bg-muted/40 text-muted-foreground gap-1"
      >
        <BookMarked size={11} />
        {`В курсе: ${first.title}`}
      </Badge>
    );
  }

  return (
    <TooltipProvider delayDuration={200}>
      <Tooltip>
        <TooltipTrigger asChild>
          <Badge
            variant="outline"
            className="border-muted/60 bg-muted/40 text-muted-foreground gap-1 cursor-help"
          >
            <BookMarked size={11} />
            {`В курсе: ${first.title}`}
            <span className="ml-1 rounded-full bg-muted-foreground/15 px-1.5 text-[10px] tabular-nums">
              +{rest.length}
            </span>
          </Badge>
        </TooltipTrigger>
        <TooltipContent className="max-w-xs">
          <p className="text-[11px] font-medium">Этот материал в курсах:</p>
          <ul className="mt-1 space-y-0.5 text-[11px]">
            {/* key=courseId — у автора могут быть курсы с одинаковыми title. */}
            {courses.map((c) => (
              <li key={c.courseId}>• {c.title}</li>
            ))}
          </ul>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
