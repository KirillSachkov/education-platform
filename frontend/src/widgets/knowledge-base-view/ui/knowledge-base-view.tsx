"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import {
  MaterialCard,
  MATERIAL_KINDS_WITH_ALL,
  materialsCardMetaQueryOptions,
  type MaterialKind,
} from "@/entities/material";
import {
  searchDocumentsCursorInfiniteQueryOptions,
  searchDocumentsInfiniteQueryOptions,
} from "@/entities/search";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { BookmarkStatusProvider, BookmarkToggleButton } from "@/entities/bookmark";
import {
  SearchTagPicker,
  SearchHighlightedMarkdown,
  searchDocumentToMaterialFeedItem,
  pickSearchPreviewSnippet,
} from "@/features/global-search";
import { EntityTypes } from "@/shared/config/entity-types";
import { tagsQueryOptions, type TagDto } from "@/entities/tag";
import { routes } from "@/shared/config/routes";
import { useDebouncedValue, useFreeOnlyParam } from "@/shared/hooks";
import { FreeOnlyToggle } from "@/shared/ui/components";
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
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useSession } from "next-auth/react";
import { useTrackGrowthView } from "@/shared/analytics";
import { resolveKnowledgeBaseAccessFilter } from "../lib/resolve-access-filter";

export function KnowledgeBaseView() {
  useTrackGrowthView(
    { name: "catalog_view", properties: { catalog_kind: "knowledge_base" } },
    "catalog:knowledge-base",
  );
  // Ctrl+K пробрасывает фильтры сюда через ?search=...&tagIds=a,b,c. Читаем один раз
  // на первом рендере, дальше UI живёт локальным state'ом (URL не обновляется при
  // ручных изменениях — ссылка остаётся «снимком» того, что передал поиск).
  const searchParams = useSearchParams();
  const initialSearch = searchParams.get("search") ?? "";
  const initialTagIdsParam = searchParams.get("tagIds") ?? "";
  const initialTagIds = initialTagIdsParam ? initialTagIdsParam.split(",").filter(Boolean) : [];

  const [kindFilter, setKindFilter] = useState<MaterialKind | "all">("all");
  const [searchInput, setSearchInput] = useState(initialSearch);
  const search = useDebouncedValue(searchInput, 300);
  const [selectedTags, setSelectedTags] = useState<TagDto[]>([]);
  const [freeOnly, setFreeOnly] = useFreeOnlyParam();

  // Подтягиваем заголовки тегов по ID, которые пришли из URL. В selectedTags кладём
  // только тех, кого реально зарезолвили — иначе чипы отрисуются без названий.
  useHydrateTagsFromIds(initialTagIds, setSelectedTags);

  // КБ полностью переведена на Typesense (см. SearchCursor.cs). Два режима:
  //   - text search → relevance ranking (page-based, max_hits=10_000 — достаточно)
  //   - browse (пустой поиск) → keyset cursor по updated_at_ticks:desc (бесконечная глубина)
  const hasTextSearch = search.trim().length > 0;
  const sharedFilters = {
    entityTypes: [EntityTypes.MATERIAL],
    tagIds: selectedTags.length > 0 ? selectedTags.map((t) => t.id) : undefined,
    search: hasTextSearch ? search.trim() : undefined,
    materialKind: kindFilter === "all" ? undefined : kindFilter,
    // Landing promises immediate value without registration, so this surface uses
    // SearchService's strict PUBLIC-only filter. The legacy `free` filter also includes
    // REGISTERED content and is intentionally retained for other clients (#814).
    accessFilter: resolveKnowledgeBaseAccessFilter(freeOnly),
    pageSize: 20,
  };

  const relevanceQuery = useInfiniteQuery({
    ...searchDocumentsInfiniteQueryOptions(sharedFilters),
    enabled: hasTextSearch,
  });
  const browseQuery = useInfiniteQuery({
    ...searchDocumentsCursorInfiniteQueryOptions(sharedFilters),
    enabled: !hasTextSearch,
  });
  const activeQuery = hasTextSearch ? relevanceQuery : browseQuery;

  const { data, isLoading, isFetching, error, hasNextPage, fetchNextPage, isFetchingNextPage } =
    activeQuery;

  const searchHits = data?.pages.flatMap((page) => page.hits) ?? [];
  const mappedMaterials = searchHits.map((hit) => searchDocumentToMaterialFeedItem(hit.document));
  const isRefreshing = isFetching && !isFetchingNextPage && !isLoading;
  const materialIds = mappedMaterials.map((m) => m.id);
  const normalized = search.trim().toLowerCase();

  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: isAuthenticated && materialIds.length > 0,
  });

  // Search-документы Typesense не несут views/duration — добираем одним батчем из ECS
  // и мёржим в карточки (#500). Деградация мягкая: без меты карточки рендерятся как раньше.
  const { data: cardMeta } = useQuery(materialsCardMetaQueryOptions(materialIds));
  const materials = cardMeta
    ? mappedMaterials.map((m) => {
        const meta = cardMeta.get(m.id);
        return meta
          ? {
              ...m,
              viewsCount: meta.viewsCount,
              durationSeconds: meta.durationSeconds,
              authorDisplayName: meta.authorDisplayName,
              authorAvatarUrl: meta.authorAvatarUrl,
            }
          : m;
      })
    : mappedMaterials;

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

  return (
    <div className="mx-auto max-w-6xl p-4 sm:p-6 space-y-6">
      <Breadcrumb>
        <BreadcrumbList>
          <BreadcrumbItem>
            <BreadcrumbLink asChild>
              <Link href={routes.home}>Главная</Link>
            </BreadcrumbLink>
          </BreadcrumbItem>
          <BreadcrumbSeparator />
          <BreadcrumbItem>
            <BreadcrumbPage>База знаний</BreadcrumbPage>
          </BreadcrumbItem>
        </BreadcrumbList>
      </Breadcrumb>

      <div className="flex items-baseline gap-3">
        <h1 className="text-2xl font-bold">База знаний</h1>
        {!isLoading && (data?.pages[0]?.totalCount ?? 0) > 0 && (
          <span className="text-sm text-muted-foreground">{data?.pages[0]?.totalCount}</span>
        )}
      </div>

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
              aria-label="Поиск по базе знаний"
            />
          </div>

          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <div className="flex min-w-0 flex-1 gap-1.5 overflow-x-auto pb-1 sm:flex-none sm:flex-wrap sm:overflow-visible sm:pb-0">
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

            <FreeOnlyToggle active={freeOnly} onChange={setFreeOnly} />
          </div>
        </div>

        <SearchTagPicker
          selectedTags={selectedTags}
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

      {error ? (
        <ErrorCard error={error} className="py-16" />
      ) : isLoading ? (
        <div className="space-y-3">
          {[0, 1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-28 rounded-xl" />
          ))}
        </div>
      ) : materials.length > 0 ? (
        <>
          <BookmarkStatusProvider
            items={materials
              .filter((m): m is typeof m & { courseId: string } => !!m.courseId)
              .map((m) => ({
                courseId: m.courseId,
                entityType: "Material",
                entityId: m.id,
              }))}
          >
            <div className="@container">
              <div className="grid grid-cols-1 gap-2.5 sm:gap-3">
                {materials.map((material, index) => {
                  // В text-search режиме показываем подсвеченный сниппет совпадения
                  // (из highlights хита, выровнен по индексу с materials). В browse
                  // режиме highlights нет → карточка рисует обычный preview.
                  const snippet = hasTextSearch
                    ? pickSearchPreviewSnippet(searchHits[index]?.highlights ?? [])
                    : null;

                  return (
                    <MaterialCard
                      key={material.id}
                      material={material}
                      viewedAt={viewedMap?.get(material.id)?.viewedAt}
                      highlightSnippet={
                        snippet ? (
                          <SearchHighlightedMarkdown className="[&_p]:mb-0 [&_p]:text-sm [&_p]:leading-snug">
                            {snippet}
                          </SearchHighlightedMarkdown>
                        ) : undefined
                      }
                      renderBookmark={({ courseId, materialId, className }) => (
                        <BookmarkToggleButton
                          courseId={courseId}
                          entityType="Material"
                          entityId={materialId}
                          className={className}
                        />
                      )}
                      href={routes.materialDetail(material.id, {
                        from: { kind: "space-knowledge-base" },
                      })}
                    />
                  );
                })}
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
          <CardContent className="py-10 text-center text-muted-foreground">
            {normalized
              ? "Ничего не найдено по запросу"
              : kindFilter !== "all"
                ? "Нет материалов этого типа"
                : "Автор пока не опубликовал материалы"}
          </CardContent>
        </Card>
      )}
    </div>
  );
}

/**
 * Гидрирует selectedTags по массиву ID, полученному из URL (?tagIds=a,b,c).
 * Один batch-запрос `tagsQueryOptions.byIds` (GET /tags/batch/) вместо N
 * параллельных byId (#512), затем единоразово (пока selectedTags ещё пуст)
 * подставляет результат в state. Любое последующее ручное редактирование чипов
 * пользователем не перезаписывается — проверка `prev.length === 0` гарантирует,
 * что hydrate срабатывает только на старте.
 */
function useHydrateTagsFromIds(
  tagIds: string[],
  setSelectedTags: React.Dispatch<React.SetStateAction<TagDto[]>>,
) {
  const { data: resolvedTags } = useQuery(tagsQueryOptions.byIds(tagIds));

  useEffect(() => {
    if (!resolvedTags || resolvedTags.length === 0) return;
    setSelectedTags((prev) => (prev.length === 0 ? resolvedTags : prev));
  }, [resolvedTags, setSelectedTags]);
}
