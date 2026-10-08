"use client";

import { useInfiniteQuery, useQuery, type UseInfiniteQueryOptions } from "@tanstack/react-query";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { useSession } from "next-auth/react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { RowCardSkeleton } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { cn } from "@/shared/lib/css";
import { authorMaterialsFeedQueryOptions, courseMaterialsFeedQueryOptions } from "../api";
import { MaterialCard } from "./material-card";
import type { MaterialFeedItemDto, MaterialFeedScope, MaterialKind } from "../types";

type MaterialFeedSource =
  | { kind: "author"; authorId: string; scope?: MaterialFeedScope }
  | { kind: "course"; courseId: string };

interface MaterialFeedProps {
  source: MaterialFeedSource;
  title?: string;
  description?: string;
  /**
   * `all` — show kind filter tabs.
   * `none` — hide filter bar (compact embed).
   */
  filters?: "all" | "none";
  /** Show author-scope segmented control (Все / Из моих курсов). */
  showScopeSwitch?: boolean;
  initialScope?: MaterialFeedScope;
  /** Hide outer section wrapper when embedded in a larger page. */
  compact?: boolean;
  limit?: number;
  /**
   * Render bookmark control for each card — feature-layer slot. Entity layer
   * stays feature-free per FSD; caller provides the button.
   */
  renderBookmark?: (args: {
    courseId: string;
    materialId: string;
    className?: string;
  }) => ReactNode;
  /**
   * Optional wrapper around rendered cards — used by feature layer to inject
   * batched providers (e.g. BookmarkStatusProvider) that depend on the loaded
   * item list. Keeps the entity layer free of feature-level imports.
   */
  renderItemsWrapper?: (items: MaterialFeedItemDto[], children: ReactNode) => ReactNode;
}

const KIND_TABS: Array<{ value: MaterialKind | "all"; label: string }> = [
  { value: "all", label: "Все" },
  { value: "ARTICLE", label: "Статьи" },
  { value: "VIDEO", label: "Видео" },
  { value: "NOTE", label: "Заметки" },
  { value: "STREAM", label: "Эфиры" },
];

export function MaterialFeed({
  source,
  title = "Лента материалов",
  description,
  filters = "all",
  showScopeSwitch = false,
  initialScope = "all",
  compact = false,
  limit = 15,
  renderBookmark,
  renderItemsWrapper,
}: MaterialFeedProps) {
  const [kindFilter, setKindFilter] = useState<MaterialKind | "all">("all");
  const [scope, setScope] = useState<MaterialFeedScope>(initialScope);
  const kind = kindFilter === "all" ? undefined : kindFilter;

  const queryOptions =
    source.kind === "author"
      ? authorMaterialsFeedQueryOptions(source.authorId, {
          limit,
          kind,
          scope: showScopeSwitch ? scope : (source.scope ?? "all"),
        })
      : courseMaterialsFeedQueryOptions(source.courseId, { limit, kind });

  const {
    data,
    isLoading,
    isError,
    error,
    refetch,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useInfiniteQuery(
    queryOptions as UseInfiniteQueryOptions<unknown, Error, { items: MaterialFeedItemDto[] }>,
  );

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

  const items = data?.items ?? [];
  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  const materialIds = items.map((i) => i.id);
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: isAuthenticated && materialIds.length > 0,
  });

  return (
    <section className={cn(compact ? "space-y-3" : "space-y-5")}>
      {(title || filters !== "none" || showScopeSwitch) && (
        <header className="flex flex-col gap-3">
          {(title || description) && (
            <div>
              {title && (
                <h2 className="text-base sm:text-lg md:text-xl font-semibold tracking-tight">
                  {title}
                </h2>
              )}
              {description && (
                <p className="text-xs sm:text-sm text-muted-foreground mt-0.5">{description}</p>
              )}
            </div>
          )}

          {(filters === "all" || showScopeSwitch) && (
            <div className="flex flex-wrap items-center gap-3">
              {showScopeSwitch && (
                <div className="inline-flex items-center rounded-xl border border-border/60 bg-card p-0.5 text-xs shadow-sm">
                  <ScopeChip active={scope === "all"} onClick={() => setScope("all")}>
                    Все материалы
                  </ScopeChip>
                  <ScopeChip active={scope === "enrolled"} onClick={() => setScope("enrolled")}>
                    Из моих курсов
                  </ScopeChip>
                </div>
              )}
              {filters === "all" && (
                <div className="flex flex-wrap items-center gap-1.5 overflow-x-auto">
                  {KIND_TABS.map((t) => (
                    <KindChip
                      key={t.value}
                      active={kindFilter === t.value}
                      onClick={() => setKindFilter(t.value)}
                    >
                      {t.label}
                    </KindChip>
                  ))}
                </div>
              )}
            </div>
          )}
        </header>
      )}

      {isLoading && <RowCardSkeleton count={3} />}

      {isError && (
        <EmptyState
          variant="dashed"
          icon={Icons.error}
          title="Не удалось загрузить ленту"
          description={error?.message ?? "Попробуйте обновить страницу."}
          action={
            <Button variant="outline" onClick={() => void refetch()}>
              Обновить
            </Button>
          }
        />
      )}

      {!isLoading && !isError && items.length === 0 && (
        <EmptyState
          variant="dashed"
          icon={Icons.library}
          title="Материалов пока нет"
          description={
            scope === "enrolled"
              ? "Записанных курсов с материалами не найдено."
              : "Автор ещё не опубликовал материалы по этому фильтру."
          }
        />
      )}

      {items.length > 0 &&
        (() => {
          const cards = (
            <div className={compact ? "space-y-2" : "space-y-3"}>
              {items.map((item) => (
                <MaterialCard
                  key={item.id}
                  material={item}
                  viewedAt={viewedMap?.get(item.id)?.viewedAt}
                  renderBookmark={renderBookmark}
                />
              ))}

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
            </div>
          );
          return renderItemsWrapper ? renderItemsWrapper(items, cards) : cards;
        })()}
    </section>
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

function ScopeChip({
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
        "px-3 py-1.5 rounded-lg text-xs font-medium transition-colors whitespace-nowrap",
        active ? "bg-primary/15 text-primary" : "text-muted-foreground hover:text-foreground",
      )}
    >
      {children}
    </button>
  );
}
