"use client";

import { type TagDto } from "@/entities/tag";
import { cn } from "@/shared/lib/css";
import { useCourseContext } from "@/shared/providers/course-id-provider";
import { useGlobalSearchStore, type GlobalSearchScope } from "@/shared/lib/global-search-store";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { DismissableLayer } from "@radix-ui/react-dismissable-layer";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { useDebouncedCallback } from "use-debounce";
import { addRecentSearch } from "../model/recent-searches-store";
import { useGlobalSearch } from "../model/use-global-search";
import { SearchList } from "./search-list";
import { resolveSearchItemHref } from "./search-item";
import { SearchTagPicker } from "./search-tag-picker";

export function GlobalSearch() {
  const router = useRouter();
  const courseContext = useCourseContext();
  const rootRef = useRef<HTMLDivElement | null>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const focusFrameRef = useRef<number | null>(null);

  const open = useGlobalSearchStore((state) => state.open);
  const query = useGlobalSearchStore((state) => state.query);
  const search = useGlobalSearchStore((state) => state.search);
  const selectedTags = useGlobalSearchStore((state) => state.selectedTags);
  const selectedEntityType = useGlobalSearchStore((state) => state.selectedEntityType);
  const rawScope = useGlobalSearchStore((state) => state.scope);
  const openSearch = useGlobalSearchStore((state) => state.openSearch);
  const closeSearch = useGlobalSearchStore((state) => state.closeSearch);
  const setQuery = useGlobalSearchStore((state) => state.setQuery);
  const setSearch = useGlobalSearchStore((state) => state.setSearch);
  const setScope = useGlobalSearchStore((state) => state.setScope);
  const addSelectedTag = useGlobalSearchStore((state) => state.addSelectedTag);
  const removeSelectedTag = useGlobalSearchStore((state) => state.removeSelectedTag);

  // Контекст маршрута диктует дефолтный scope: в курсе → "course", иначе → "everywhere".
  // Single-tenant: автор всегда один, отдельного «author scope» больше не показываем.
  const hasCourseContext = !!courseContext;
  const defaultScope: GlobalSearchScope = hasCourseContext ? "course" : "everywhere";
  const requestedScope = rawScope ?? defaultScope;
  const scope: GlobalSearchScope = (() => {
    if (requestedScope === "course" && !hasCourseContext) return "everywhere";
    if (requestedScope === "author") return hasCourseContext ? "course" : "everywhere";
    return requestedScope;
  })();

  const courseIdForScope = scope === "course" ? courseContext?.courseId : undefined;

  const debouncedSetQuery = useDebouncedCallback((value: string) => {
    setSearch(value.trim());
  }, 250);

  // Mobile: блокируем прокрутку body пока открыт full-screen overlay. На desktop панель
  // inline-dropdown и этого не требуется, но overflow-hidden на больших экранах тоже не
  // вредит (пользователь всё равно видит только результаты поиска).
  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  // Keyboard shortcut: "/" focuses the search.
  useEffect(() => {
    function handler(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      const tag = target?.tagName;
      const isTyping =
        tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || target?.isContentEditable;

      if (event.key === "/" && !isTyping) {
        event.preventDefault();
        openSearch();
        focusInputSoon();
      }
    }
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
    // focusInputSoon and openSearch are stable (refs/zustand); lint cleared by compiler.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function focusInputSoon() {
    if (focusFrameRef.current !== null) {
      window.cancelAnimationFrame(focusFrameRef.current);
    }

    focusFrameRef.current = window.requestAnimationFrame(() => {
      focusFrameRef.current = null;
      inputRef.current?.focus();
    });
  }

  function handleInputRef(node: HTMLInputElement | null) {
    inputRef.current = node;

    if (!node) {
      if (focusFrameRef.current !== null) {
        window.cancelAnimationFrame(focusFrameRef.current);
        focusFrameRef.current = null;
      }
      return;
    }

    if (open) {
      focusInputSoon();
    }
  }

  const { documentsQuery, hits, hasDocumentQuery, isError, totalResultsCount } = useGlobalSearch({
    open,
    search,
    selectedEntityType,
    selectedTags,
    authorId: undefined,
    courseId: courseIdForScope,
  });

  const [activeIndex, setActiveIndex] = useState(-1);

  // Сбросить активный пункт, когда меняется выдача (новый запрос / новый набор тегов).
  // Render-time check вместо useEffect: React официально поддерживает setState
  // во время рендера для производного состояния, и это дешевле, чем effect-цикл.
  const searchKey = `${search}|${selectedTags.map((t) => t.id).join(",")}|${selectedEntityType}`;
  const prevSearchKeyRef = useRef(searchKey);
  if (prevSearchKeyRef.current !== searchKey) {
    prevSearchKeyRef.current = searchKey;
    setActiveIndex(-1);
  }

  function handleDismiss() {
    debouncedSetQuery.flush();
    closeSearch();
  }

  function handleQueryChange(value: string) {
    setQuery(value);
    debouncedSetQuery(value);
    if (!open) {
      openSearch();
    }
  }

  function handleRemoveTag(tagId: string) {
    removeSelectedTag(tagId);
  }

  function handleSelectTag(tag: TagDto) {
    addSelectedTag({ id: tag.id, title: tag.title });
  }

  function handleNavigate(href: string) {
    if (search.trim().length > 0) {
      addRecentSearch(search);
    }
    closeSearch();
    router.push(href);
  }

  function handleSelectRecent(value: string) {
    setQuery(value);
    setSearch(value);
    focusInputSoon();
  }

  function handleInputKeyDown(event: ReactKeyboardEvent<HTMLInputElement>) {
    if (event.key === "Backspace" && query.length === 0 && selectedTags.length > 0) {
      const lastTag = selectedTags[selectedTags.length - 1];
      if (lastTag) {
        handleRemoveTag(lastTag.id);
      }
      return;
    }

    if (event.key === "Escape") {
      event.preventDefault();
      handleDismiss();
      return;
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      if (hits.length === 0) return;
      setActiveIndex((prev) => (prev + 1) % hits.length);
      return;
    }

    if (event.key === "ArrowUp") {
      event.preventDefault();
      if (hits.length === 0) return;
      setActiveIndex((prev) => (prev <= 0 ? hits.length - 1 : prev - 1));
      return;
    }

    if (event.key === "Enter" && activeIndex >= 0 && activeIndex < hits.length) {
      const hit = hits[activeIndex];
      if (!hit) return;
      const href = resolveSearchItemHref(hit.document);
      if (href) {
        event.preventDefault();
        handleNavigate(href);
      }
    }
  }

  return (
    <div ref={rootRef} className="relative w-full">
      {/* Mobile: icon button that opens the full-screen overlay (input lives inside the overlay).
          Desktop: inline input with /-shortcut hints. The mobile variant avoids the squashed
          placeholder / ghost-looking empty field in narrow headers. */}
      <button
        type="button"
        onClick={openSearch}
        aria-label="Поиск"
        className="inline-flex size-9 items-center justify-center rounded-xl text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground md:hidden"
      >
        <Icons.search size={17} />
      </button>
      <div className="relative hidden md:block">
        <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-4 text-muted-foreground">
          <Icons.search size={17} />
        </div>
        <Input
          ref={handleInputRef}
          data-global-search-input="true"
          value={query}
          onFocus={openSearch}
          onClick={openSearch}
          onChange={(event) => handleQueryChange(event.target.value)}
          onKeyDown={handleInputKeyDown}
          placeholder="Поиск по урокам, задачам и тегам"
          className={cn(
            "h-11 rounded-[1.15rem] border-border/60 bg-background/78 pl-11 pr-12 text-[15px] shadow-none",
            open && "border-teal/50 bg-background ring-2 ring-teal/20",
          )}
        />
        <div className="pointer-events-none absolute inset-y-0 right-3 flex items-center">
          <span className="rounded-full border border-border/60 bg-background/50 px-2 py-1 text-[10px] font-semibold tracking-[0.18em] text-muted-foreground uppercase">
            /
          </span>
        </div>
      </div>

      {open && (
        <DismissableLayer
          onEscapeKeyDown={handleDismiss}
          onInteractOutside={(event) => {
            const target = event.target as Element | null;
            if (target && rootRef.current?.contains(target)) {
              event.preventDefault();
              return;
            }

            if (target?.closest('[data-global-search-tag-picker="true"]')) {
              event.preventDefault();
              return;
            }

            handleDismiss();
          }}
          className={cn(
            // На mobile (<md) — full-screen overlay с собственным input'ом; на desktop —
            // inline dropdown под header-input'ом. Отдельный рендер для мобилы избегает
            // обрезанных фасет-табов, tag-picker'а и прочих узких артефактов.
            //
            // Desktop: панель светлее surface (#161513) — в дарк-теме elevation = lighter.
            // Ширина совпадает с контейнером input'а (left-0 + right-0), чтобы визуально
            // dropdown был той же ширины, что и сама строка поиска.
            // h-[100dvh] (dynamic viewport) shrinks when the mobile keyboard opens so content
            // doesn't disappear behind it; `fixed top-0 left-0 right-0` pins to the top so the
            // header row stays put while the scroll region fills the visible area above keyboard.
            "fixed top-0 left-0 right-0 h-[100dvh] z-50 flex flex-col overflow-hidden border-border/60 bg-popover/98 shadow-[0_40px_100px_-40px_rgba(0,0,0,0.85)] backdrop-blur-xl",
            "md:absolute md:inset-auto md:left-0 md:right-0 md:top-[calc(100%+0.6rem)] md:h-auto md:w-auto md:rounded-[1.5rem] md:border md:ring-1 md:ring-border/60",
          )}
        >
          <div className="pointer-events-none absolute inset-x-0 top-0 h-20 bg-[linear-gradient(180deg,rgba(45,212,191,0.05),transparent)]" />

          {/* Mobile-only header: close + input (на desktop этот блок скрыт, используется header-input'ом выше) */}
          <div className="relative flex items-center gap-2 border-b border-border/60 bg-background/70 px-3 py-3 md:hidden">
            <button
              type="button"
              onClick={handleDismiss}
              aria-label="Закрыть поиск"
              className="inline-flex size-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
            >
              <Icons.chevronLeft size={20} />
            </button>
            <div className="relative min-w-0 flex-1">
              <div className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-muted-foreground">
                <Icons.search size={16} />
              </div>
              <Input
                autoFocus
                value={query}
                onChange={(event) => handleQueryChange(event.target.value)}
                onKeyDown={handleInputKeyDown}
                placeholder="Поиск"
                className="h-10 rounded-full border-border/60 bg-background/78 pl-9 pr-3 shadow-none"
              />
            </div>
          </div>

          <div className="relative flex-1 space-y-3 overflow-y-auto px-3 py-3 md:space-y-4 md:p-4">
            <div className="flex flex-col gap-2.5 md:gap-3.5">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="text-xs text-muted-foreground">
                  {hasDocumentQuery ? (
                    <>
                      Найдено{" "}
                      <span className="font-semibold text-foreground">{totalResultsCount}</span>{" "}
                      {pluralizeResults(totalResultsCount)}
                    </>
                  ) : (
                    "Добавь теги или начни вводить запрос"
                  )}
                </div>

                <ScopeToggle
                  scope={scope}
                  onScopeChange={setScope}
                  hasCourseContext={hasCourseContext}
                />
              </div>

              <SearchTagPicker
                selectedTags={selectedTags.map((tag) => ({
                  id: tag.id,
                  title: tag.title,
                  slug: "",
                  kind: "canon",
                }))}
                onRemove={handleRemoveTag}
                onAdd={handleSelectTag}
              />
            </div>

            <SearchList
              isLoading={documentsQuery.isLoading}
              isFetchingNextPage={documentsQuery.isFetchingNextPage}
              isError={isError}
              hasNextPage={Boolean(documentsQuery.hasNextPage)}
              hasQuery={hasDocumentQuery}
              hits={hits}
              query={search}
              activeIndex={activeIndex}
              onActiveIndexChange={setActiveIndex}
              onNavigate={handleNavigate}
              onRetry={() => void documentsQuery.refetch()}
              onSelectRecent={handleSelectRecent}
              onReachEnd={() => {
                if (documentsQuery.hasNextPage && !documentsQuery.isFetchingNextPage) {
                  void documentsQuery.fetchNextPage();
                }
              }}
            />

            {hasDocumentQuery && (
              <KnowledgeBaseHandoff
                courseSlug={scope === "course" ? courseContext?.courseSlug : undefined}
                search={search}
                selectedTags={selectedTags}
                onNavigate={() => {
                  if (search.trim().length > 0) {
                    addRecentSearch(search);
                  }
                  closeSearch();
                }}
              />
            )}
          </div>
        </DismissableLayer>
      )}
    </div>
  );
}

function pluralizeResults(count: number): string {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return "результат";
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 10 || mod100 >= 20)) return "результата";
  return "результатов";
}

function KnowledgeBaseHandoff({
  courseSlug,
  search,
  selectedTags,
  onNavigate,
}: {
  courseSlug: string | undefined;
  search: string;
  selectedTags: { id: string; title: string }[];
  onNavigate: () => void;
}) {
  const href = buildKnowledgeBaseHref({
    courseSlug,
    search,
    tagIds: selectedTags.map((tag) => tag.id),
  });

  const label = courseSlug ? "Показать в базе знаний курса" : "Показать в базе знаний";

  return (
    <div className="flex justify-center pt-2 pb-1">
      <Link
        href={href}
        onClick={onNavigate}
        className="inline-flex items-center gap-2 rounded-full border border-border/50 bg-background/40 px-4 py-2 text-sm font-medium text-muted-foreground transition-colors hover:border-teal/40 hover:bg-teal/10 hover:text-foreground"
      >
        <Icons.layers size={14} />
        <span>{label}</span>
        <span aria-hidden>→</span>
      </Link>
    </div>
  );
}

function buildKnowledgeBaseHref({
  courseSlug,
  search,
  tagIds,
}: {
  courseSlug: string | undefined;
  search: string;
  tagIds: string[];
}) {
  const base = courseSlug ? routes.courseKnowledgeBase(courseSlug) : routes.knowledgeBase;

  const params = new URLSearchParams();
  const trimmed = search.trim();
  if (trimmed.length > 0) params.set("search", trimmed);
  if (tagIds.length > 0) params.set("tagIds", tagIds.join(","));

  const qs = params.toString();
  return qs.length > 0 ? `${base}?${qs}` : base;
}

function ScopeToggle({
  scope,
  onScopeChange,
  hasCourseContext,
}: {
  scope: GlobalSearchScope;
  onScopeChange: (scope: GlobalSearchScope) => void;
  hasCourseContext: boolean;
}) {
  // Toggle only имеет смысл когда есть курсовой контекст — там «Везде» vs «В курсе».
  if (!hasCourseContext) return null;

  return (
    <div className="inline-flex items-center rounded-lg border border-border/60 bg-background/40 p-0.5 text-xs">
      <ScopeButton active={scope === "everywhere"} onClick={() => onScopeChange("everywhere")}>
        Везде
      </ScopeButton>
      <ScopeButton active={scope === "course"} onClick={() => onScopeChange("course")}>
        В курсе
      </ScopeButton>
    </div>
  );
}

function ScopeButton({
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
        "rounded-md px-2.5 py-1 font-medium transition-colors whitespace-nowrap",
        active ? "bg-primary/15 text-primary" : "text-muted-foreground hover:text-foreground",
      )}
    >
      {children}
    </button>
  );
}
