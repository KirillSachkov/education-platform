"use client";

import { type SearchEducationDocumentDto, type SearchHit } from "@/entities/search";
import { EntityTypes } from "@/shared/config/entity-types";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import { routes } from "@/shared/config/routes";
import { useInfiniteScroll } from "@/shared/hooks";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import {
  clearRecentSearches,
  removeRecentSearch,
  useRecentSearches,
} from "../model/recent-searches-store";
import { SearchItem } from "./search-item";

interface SearchListProps {
  isLoading: boolean;
  isFetchingNextPage: boolean;
  isError: boolean;
  hasNextPage: boolean;
  hasQuery: boolean;
  hits: SearchHit<SearchEducationDocumentDto>[];
  query: string;
  activeIndex: number;
  onNavigate: (href: string) => void;
  onRetry: () => void;
  onReachEnd: () => void;
  onSelectRecent?: (query: string) => void;
  onActiveIndexChange?: (index: number) => void;
}

export function SearchList({
  isLoading,
  isFetchingNextPage,
  isError,
  hasNextPage,
  hasQuery,
  hits,
  query,
  activeIndex,
  onNavigate,
  onRetry,
  onReachEnd,
  onSelectRecent,
  onActiveIndexChange,
}: SearchListProps) {
  const cursorRef = useInfiniteScroll({
    hasNextPage,
    isFetchingNextPage,
    fetchNextPage: onReachEnd,
  });

  const isAuthenticated = useIsAuthenticated();
  // Search results are mixed entity types — only batch-fetch view status for materials.
  const materialIds = hits
    .filter((hit) => hit.document.entityType === EntityTypes.MATERIAL)
    .map((hit) => hit.document.entityId);
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: isAuthenticated && materialIds.length > 0,
  });

  if (!hasQuery) {
    return <SearchIdleState onSelectRecent={onSelectRecent} />;
  }

  if (isLoading && hits.length === 0) {
    return (
      <div className="flex min-h-[200px] items-center justify-center rounded-xl border border-border/50 bg-muted/40">
        <Icons.loading className="size-5 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex min-h-[200px] items-center justify-center rounded-xl border border-destructive/25 bg-destructive/5 px-8 py-10 text-center">
        <div className="max-w-md space-y-4">
          <div className="mx-auto inline-flex size-12 items-center justify-center rounded-2xl border border-destructive/25 bg-destructive/10">
            <Icons.error size={18} className="text-destructive" />
          </div>
          <div className="space-y-1">
            <h3 className="text-base font-semibold text-foreground">Поиск сейчас недоступен</h3>
            <p className="text-sm leading-6 text-muted-foreground">
              Не удалось получить результаты. Попробуй повторить запрос.
            </p>
          </div>
          <Button type="button" variant="outline" size="sm" onClick={onRetry}>
            Повторить
          </Button>
        </div>
      </div>
    );
  }

  if (hits.length === 0) {
    return <SearchEmptyState query={query} onNavigate={onNavigate} />;
  }

  return (
    <div role="listbox" aria-label="Результаты поиска" className="divide-y divide-border/40">
      {hits.map((hit, index) => (
        // `role="presentation"` makes the wrapper invisible to AT — SearchItem's
        // role="option" is still seen as a descendant of the listbox (ARIA spec
        // requires "contained within", not direct child). content-visibility:auto
        // would be a no-op under display:contents, so we keep the layout box but
        // present it as semantically transparent.
        <div
          key={`${hit.document.entityType}:${hit.document.entityId}`}
          role="presentation"
          style={
            index >= 6
              ? { contentVisibility: "auto", containIntrinsicSize: "120px" }
              : undefined
          }
        >
          <SearchItem
            document={hit.document}
            highlights={hit.highlights}
            query={query}
            isActive={index === activeIndex}
            highPriorityImage={index < 3}
            isViewed={
              hit.document.entityType === EntityTypes.MATERIAL
                ? (viewedMap?.get(hit.document.entityId)?.isViewed ?? false)
                : false
            }
            onNavigate={onNavigate}
            onHover={() => onActiveIndexChange?.(index)}
          />
        </div>
      ))}

      <div ref={cursorRef} className="h-1" />

      {isFetchingNextPage ? (
        <div className="flex items-center justify-center py-3 text-sm text-muted-foreground">
          <Icons.loading className="size-4 animate-spin text-muted-foreground" />
        </div>
      ) : null}
    </div>
  );
}

function SearchIdleState({ onSelectRecent }: { onSelectRecent?: (query: string) => void }) {
  const recents = useRecentSearches();

  if (recents.length === 0 || !onSelectRecent) {
    return (
      <div className="flex min-h-[200px] items-center justify-center rounded-xl border border-dashed border-border/50 bg-muted/40 px-8 py-10 text-center">
        <div className="max-w-md space-y-3">
          <div className="mx-auto inline-flex size-12 items-center justify-center rounded-2xl border border-primary/20 bg-primary/10">
            <Icons.search size={18} className="text-primary" />
          </div>
          <div className="space-y-1">
            <h3 className="text-base font-semibold text-foreground">
              Начни вводить запрос или добавь теги
            </h3>
            <p className="text-sm leading-6 text-muted-foreground">
              Поиск начнётся сразу после ввода текста или выбора хотя бы одного тега.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="rounded-xl border border-border/50 bg-muted/40 p-3">
      <div className="mb-3 flex items-center justify-between">
        <div className="text-[11px] font-semibold tracking-[0.16em] text-muted-foreground uppercase">
          Недавние запросы
        </div>
        <button
          type="button"
          className="text-[11px] font-medium text-muted-foreground hover:text-foreground"
          onClick={clearRecentSearches}
        >
          Очистить
        </button>
      </div>
      <ul className="space-y-1">
        {recents.map((q) => (
          <li
            key={q}
            className="flex items-center gap-1 rounded-xl border border-transparent hover:border-border/60 hover:bg-card/50"
          >
            <button
              type="button"
              onClick={() => onSelectRecent(q)}
              className="flex min-w-0 flex-1 items-center gap-2 px-3 py-2 text-left"
            >
              <Icons.clock size={14} className="shrink-0 text-muted-foreground" />
              <span className="truncate text-sm text-foreground">{q}</span>
            </button>
            <button
              type="button"
              aria-label={`Удалить запрос «${q}»`}
              className="mr-1 rounded-md p-1 text-muted-foreground hover:bg-muted/50 hover:text-foreground"
              onClick={() => removeRecentSearch(q)}
            >
              <Icons.close size={14} />
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

function SearchEmptyState({
  query,
  onNavigate,
}: {
  query: string;
  onNavigate: (href: string) => void;
}) {
  const isAuthenticated = useIsAuthenticated();
  const trimmed = query.trim();

  return (
    <div className="flex min-h-[200px] items-center justify-center rounded-xl border border-dashed border-border/50 bg-muted/40 px-8 py-10 text-center">
      <div className="max-w-md space-y-4">
        <div className="mx-auto inline-flex size-12 items-center justify-center rounded-2xl border border-muted-foreground/20 bg-muted/30">
          <Icons.search size={18} className="text-muted-foreground" />
        </div>
        <div className="space-y-1">
          <h3 className="text-base font-semibold text-foreground">
            {trimmed ? (
              <>
                Ничего не найдено по запросу <span className="text-primary">«{trimmed}»</span>
              </>
            ) : (
              "Ничего не найдено"
            )}
          </h3>
          <p className="text-sm leading-6 text-muted-foreground">
            Попробуй переформулировать запрос, снять часть фильтров или проверить написание.
          </p>
        </div>
        {!isAuthenticated && (
          <div className="space-y-2 rounded-xl border border-primary/20 bg-primary/5 px-3 py-2.5 text-left text-sm">
            <p className="text-foreground/80">
              Вошедшим пользователям виден дополнительный контент курсов.
            </p>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => onNavigate(routes.login)}
            >
              <Icons.login size={14} />
              Войти в аккаунт
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
