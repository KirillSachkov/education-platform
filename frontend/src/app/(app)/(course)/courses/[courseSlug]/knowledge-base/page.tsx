"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { courseCollectionsQueryOptions } from "@/entities/collection";
import {
  MaterialCard,
  MATERIAL_KINDS_WITH_ALL,
  courseMaterialTagsQueryOptions,
  courseMaterialsFeedQueryOptions,
  type MaterialKind,
} from "@/entities/material";
import { courseCurriculumQueryOptions, useCourseAccess } from "@/entities/course";
import { tagsQueryOptions, type TagDto } from "@/entities/tag";
import { CollectionGrid } from "@/entities/collection";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { BookmarkStatusProvider, BookmarkToggleButton } from "@/entities/bookmark";
import { SearchTagPicker } from "@/features/global-search";
import { useCourseId, useCourseSlug } from "@/shared/providers/course-id-provider";
import { routes } from "@/shared/config/routes";
import { useDebouncedValue } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";
import {
  Breadcrumb,
  BreadcrumbList,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/shared/ui/kit/breadcrumb";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Skeleton } from "@/shared/ui/kit/skeleton";
export default function CourseKnowledgeBasePage() {
  const courseId = useCourseId();
  const courseSlug = useCourseSlug();

  // Ctrl+K handoff: ?search=...&tagIds=a,b,c (см. buildKnowledgeBaseHref).
  const searchParams = useSearchParams();
  const initialSearch = searchParams.get("search") ?? "";
  const initialTagIdsParam = searchParams.get("tagIds") ?? "";
  const initialTagIds = initialTagIdsParam ? initialTagIdsParam.split(",").filter(Boolean) : [];

  const [kindFilter, setKindFilter] = useState<MaterialKind | "all">("all");
  const [searchInput, setSearchInput] = useState(initialSearch);
  const search = useDebouncedValue(searchInput, 300);
  const [selectedTags, setSelectedTags] = useState<TagDto[]>([]);

  useHydrateTagsFromIds(initialTagIds, setSelectedTags);

  const access = useCourseAccess(courseId);
  const { data: curriculum } = useQuery(courseCurriculumQueryOptions(courseId));
  // Collections + список материалов курса доступны анониму (с lock-иконками для
  // недоступных). База знаний курса читает курсовую программу напрямую из
  // course_materials (`GET /courses/{id}/materials/feed/`) — это canonical-источник
  // «что в курсе», не зависит от Typesense (sync lag, single-courseId-per-doc) и
  // показывает все материалы программы, включая привязанные к нескольким курсам.
  const { data: collections } = useQuery(courseCollectionsQueryOptions(courseId));
  const { data: courseTags = [], isLoading: isCourseTagsLoading } = useQuery(
    courseMaterialTagsQueryOptions(courseId),
  );
  const trimmedSearch = search.trim();
  const { data, isLoading, isFetching, hasNextPage, fetchNextPage, isFetchingNextPage } =
    useInfiniteQuery(
      courseMaterialsFeedQueryOptions(courseId, {
        limit: 20,
        kind: kindFilter === "all" ? undefined : kindFilter,
        tagIds: selectedTags.length > 0 ? selectedTags.map((t) => t.id) : undefined,
        search: trimmedSearch.length > 0 ? trimmedSearch : undefined,
      }),
    );

  const materials = data?.items ?? [];
  const isRefreshing = isFetching && !isFetchingNextPage && !isLoading;
  const materialIds = materials.map((m) => m.id);
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: access.isAuthenticated && materialIds.length > 0,
  });

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

  const hasFilters = !!search.trim() || kindFilter !== "all" || selectedTags.length > 0;

  return (
    <div className="mx-auto max-w-6xl p-4 sm:p-6 space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink asChild>
              <Link href={routes.courseOverview(courseSlug)}>{curriculum?.title ?? "Курс"}</Link>
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>База знаний</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <div className="flex items-baseline gap-3">
        <h1 className="text-2xl font-bold">База знаний курса</h1>
      </div>

      {collections && collections.length > 0 && (
        <CollectionGrid
          collections={collections}
          getHref={(collectionId) => routes.courseCollectionDetail(courseSlug, collectionId)}
          initialCount={4}
          step={4}
        />
      )}

      <div className="space-y-3 rounded-xl border border-border/60 bg-card/35 p-3">
        <div className="grid gap-3 lg:grid-cols-[minmax(16rem,1fr)_auto] lg:items-center">
          <div className="relative min-w-0">
            <Icons.search
              size={14}
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              type="search"
              inputMode="search"
              enterKeyHint="search"
              autoComplete="off"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Поиск по названию..."
              className="pl-9"
              aria-label="Поиск по базе знаний курса"
            />
          </div>

          <div className="flex min-w-0 gap-1.5 overflow-x-auto pb-1 sm:flex-wrap sm:overflow-visible sm:pb-0">
            {MATERIAL_KINDS_WITH_ALL.map((filter) => (
              <button
                key={filter.value}
                type="button"
                onClick={() => setKindFilter(filter.value)}
                aria-pressed={kindFilter === filter.value}
                className={cn(
                  "inline-flex h-11 min-h-[44px] items-center rounded-full border px-3 text-xs font-medium whitespace-nowrap transition-colors sm:h-10 sm:min-h-10",
                  "outline-none focus-visible:ring-2 focus-visible:ring-ring/60",
                  kindFilter === filter.value
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-background/55 text-muted-foreground hover:border-border hover:text-foreground",
                )}
              >
                {filter.label}
              </button>
            ))}
          </div>
        </div>

        <SearchTagPicker
          selectedTags={selectedTags}
          suggestionTags={courseTags}
          isSuggestionTagsLoading={isCourseTagsLoading}
          className="space-y-2"
          onAdd={(tag) =>
            setSelectedTags((prev) => (prev.some((t) => t.id === tag.id) ? prev : [...prev, tag]))
          }
          onRemove={(tagId) => setSelectedTags((prev) => prev.filter((t) => t.id !== tagId))}
        />
      </div>

      {isRefreshing && (
        <div className="flex items-center justify-center gap-2 rounded-lg border border-border/60 bg-card/40 py-3 text-xs text-muted-foreground">
          <Icons.loading className="size-4 animate-spin" />
          Обновляем список
        </div>
      )}

      {isLoading ? (
        <div className="space-y-3">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-28 rounded-xl" />
          ))}
        </div>
      ) : materials.length > 0 ? (
        <>
          <BookmarkStatusProvider
            items={
              access.isAuthenticated
                ? materials
                    .filter((m): m is typeof m & { courseId: string } => !!m.courseId)
                    .map((m) => ({
                      courseId: m.courseId,
                      entityType: "Material",
                      entityId: m.id,
                    }))
                : []
            }
          >
            <div className="@container">
              <div className="grid grid-cols-1 gap-2.5 sm:gap-3">
                {materials.map((material) => (
                  <MaterialCard
                    key={material.id}
                    material={material}
                    viewedAt={viewedMap?.get(material.id)?.viewedAt}
                    renderBookmark={
                      access.isAuthenticated
                        ? ({ courseId, materialId, className }) => (
                            <BookmarkToggleButton
                              courseId={courseId}
                              entityType="Material"
                              entityId={materialId}
                              className={className}
                            />
                          )
                        : undefined
                    }
                    href={routes.courseMaterial(courseSlug, material.id, {
                      from: { kind: "course-knowledge-base", courseSlug },
                    })}
                  />
                ))}
              </div>
            </div>
          </BookmarkStatusProvider>
          {hasNextPage && (
            <div
              ref={sentinelRef}
              className="flex items-center justify-center py-6 text-xs text-muted-foreground"
            >
              {isFetchingNextPage ? (
                <span className="inline-flex items-center gap-2">
                  <Icons.loading className="size-4 animate-spin" />
                  Загружаем ещё…
                </span>
              ) : (
                <span>Прокрутите, чтобы загрузить больше</span>
              )}
            </div>
          )}
        </>
      ) : (
        <Card>
          <CardContent className="flex flex-col items-center py-10 text-center text-muted-foreground">
            <Icons.scroll className="mb-3 size-10 text-muted-foreground/40" />
            <p className="font-medium">
              {hasFilters ? "По фильтрам ничего не нашлось" : "Нет прикреплённых материалов"}
            </p>
            <p className="mt-1 text-sm">
              {hasFilters
                ? "Попробуйте изменить запрос или снять часть тегов"
                : "Материалы появятся здесь после добавления автором"}
            </p>
          </CardContent>
        </Card>
      )}
    </div>
  );
}

function useHydrateTagsFromIds(
  tagIds: string[],
  setSelectedTags: React.Dispatch<React.SetStateAction<TagDto[]>>,
) {
  // Один batch-запрос GET /tags/batch/ вместо N параллельных byId (#512).
  // enabled внутри byIds — при пустом tagIds запрос не уходит вовсе.
  const { data: resolvedTags } = useQuery(tagsQueryOptions.byIds(tagIds));

  useEffect(() => {
    if (!resolvedTags || resolvedTags.length === 0) return;
    setSelectedTags((prev) => (prev.length === 0 ? resolvedTags : prev));
  }, [resolvedTags, setSelectedTags]);
}
