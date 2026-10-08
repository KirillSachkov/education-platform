"use client";

import { MaterialCard, MATERIAL_KINDS_WITH_ALL, type MaterialKind } from "@/entities/material";
import { userProgressQueryOptions } from "@/entities/user-progress";
import { BookmarkToggleButton } from "@/entities/bookmark";
import { getErrorMessage } from "@/shared/api";
import { useDebouncedValue } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Input } from "@/shared/ui/kit/input";
import { Icons } from "@/shared/ui/icons";
import { Tabs, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { useQuery } from "@tanstack/react-query";
import { Loader2, Plus, RefreshCw, ScrollText } from "lucide-react";
import Link from "next/link";
import { useSession } from "next-auth/react";
import { useState } from "react";
import {
  type MaterialsTab,
  type MaterialsViewMode,
  useMaterialsFeed,
} from "../model/use-materials-feed";

interface MaterialsPageProps {
  mode: MaterialsViewMode;
  onDeleteMaterial?: (materialId: string) => void;
  isDeletePending?: boolean;
}

export function MaterialsPage({ mode, onDeleteMaterial, isDeletePending }: MaterialsPageProps) {
  const [activeTab, setActiveTab] = useState<MaterialsTab>(mode === "teaching" ? "mine" : "public");
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput, 300);
  const [kindFilter, setKindFilter] = useState<MaterialKind | "all">("all");

  const trimmedSearch = search.trim();
  // Drive UI chrome (counter label, toolbar visibility, which empty-state) off the raw
  // input so it reacts instantly — deriving it from the 300ms-debounced value lags the
  // label/empty-state behind the user clearing the field. trimmedSearch feeds the query.
  const hasActiveFilter = searchInput.trim().length > 0 || kindFilter !== "all";
  const clearFilters = () => {
    setSearchInput("");
    setKindFilter("all");
  };

  const {
    items,
    totalCount,
    isLoading,
    isFetching,
    isFetchingNextPage,
    hasNextPage,
    error,
    fetchNextPage,
    refetch,
    cursorRef,
  } = useMaterialsFeed({
    mode,
    activeTab,
    search: trimmedSearch || undefined,
    kind: kindFilter === "all" ? undefined : kindFilter,
  });

  const isTeaching = mode === "teaching";
  const detailRoute = isTeaching
    ? (id: string) => routes.authorMaterialEdit(id)
    : routes.knowledgeBaseMaterial;

  const session = useSession();
  const isAuthenticated = session.status === "authenticated";
  const materialIds = items.map((m) => m.id);
  const { data: viewedMap } = useQuery({
    ...userProgressQueryOptions.materialViewStatusOptions(materialIds),
    enabled: !isTeaching && isAuthenticated && materialIds.length > 0,
  });

  return (
    <div className="mx-auto max-w-5xl p-4 sm:p-6">
      <div className="mb-8 flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <h1 className="text-2xl font-bold">База знаний</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            {isTeaching
              ? "Управляйте базой знаний: создавайте, публикуйте и редактируйте материалы."
              : "Публичные материалы платформы для самостоятельного изучения и быстрого повторения тем."}
          </p>
          {!isLoading && !error && (
            <p className="text-sm text-muted-foreground">
              {hasActiveFilter ? "Найдено" : "Всего материалов"}: {totalCount}
            </p>
          )}
        </div>

        {isTeaching && (
          <Button
            asChild
            className="border-0 bg-gradient-primary text-primary-foreground hover:opacity-90"
          >
            <Link href={routes.authorKnowledgeBaseCreate}>
              <Plus size={16} />
              Создать материал
            </Link>
          </Button>
        )}
      </div>

      {isTeaching && (
        <div className="mb-6">
          <Tabs value={activeTab} onValueChange={(value) => setActiveTab(value as MaterialsTab)}>
            <TabsList>
              <TabsTrigger value="mine">Все</TabsTrigger>
              <TabsTrigger value="public">Пространство</TabsTrigger>
              <TabsTrigger value="private">Черновики</TabsTrigger>
            </TabsList>
          </Tabs>
        </div>
      )}

      {/*
        Toggle на платформенной /knowledge-base не рендерим — endpoint /materials/
        (scope=public) хардкодит `access_type='PUBLIC'`, фильтр был бы no-op. Полноценный
        free-only filter живёт на пространстве автора (/@slug/knowledge-base), туда же
        ссылается виджет с лендинга. Если/когда расширим /materials/ до PUBLIC+REGISTERED
        с lock-иконками — вернём toggle.
      */}

      {!error && (isLoading || items.length > 0 || hasActiveFilter) && (
        <div className="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative min-w-0 flex-1">
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
              aria-label="Поиск по материалам"
            />
          </div>
          <div className="flex flex-wrap gap-1.5 overflow-x-auto">
            {MATERIAL_KINDS_WITH_ALL.map((filter) => (
              <button
                key={filter.value}
                type="button"
                onClick={() => setKindFilter(filter.value)}
                aria-pressed={kindFilter === filter.value}
                className={cn(
                  "inline-flex h-11 min-h-[44px] items-center rounded-full border px-4 text-xs font-medium whitespace-nowrap transition-colors",
                  "outline-none focus-visible:ring-2 focus-visible:ring-ring/60",
                  kindFilter === filter.value
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground hover:border-border hover:text-foreground",
                )}
              >
                {filter.label}
              </button>
            ))}
          </div>
        </div>
      )}

      {isLoading && (
        <div className="flex justify-center py-16">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isLoading && error && (
        <div className="rounded-2xl border border-border/70 bg-card/80 p-6 text-center">
          <p className="font-medium text-destructive">Не удалось загрузить материалы</p>
          <p className="mt-2 text-sm text-muted-foreground">
            {getErrorMessage(error, "Попробуйте обновить страницу позже.")}
          </p>
          <div className="mt-4 flex justify-center">
            <Button variant="outline" onClick={() => void refetch()}>
              <RefreshCw size={14} />
              Повторить
            </Button>
          </div>
        </div>
      )}

      {!isLoading && !error && items.length === 0 && hasActiveFilter && (
        <EmptyState
          icon={Icons.searchEmpty}
          title="Ничего не найдено"
          description="Попробуйте изменить запрос или сбросить фильтры."
          action={
            <Button variant="outline" onClick={clearFilters}>
              Сбросить фильтры
            </Button>
          }
          className="rounded-2xl border border-dashed border-border/70 bg-card/70 px-6 py-16"
        />
      )}

      {!isLoading && !error && items.length === 0 && !hasActiveFilter && (
        <EmptyState
          icon={ScrollText}
          title={
            isTeaching ? "В этом разделе пока нет материалов" : "Пока нет опубликованных материалов"
          }
          description={
            isTeaching
              ? "Создайте первый материал, чтобы наполнить базу полезных ресурсов."
              : "Опубликованные материалы появятся здесь автоматически."
          }
          action={
            isTeaching ? (
              <Button asChild>
                <Link href={routes.authorKnowledgeBaseCreate}>
                  <Plus size={16} />
                  Создать материал
                </Link>
              </Button>
            ) : null
          }
          className="rounded-2xl border border-dashed border-border/70 bg-card/70 px-6 py-16"
        />
      )}

      {!isLoading && !error && items.length > 0 && (
        <>
          <div
            className={cn(
              "space-y-3 transition-opacity",
              isFetching && !isFetchingNextPage && "opacity-60",
            )}
          >
            {items.map((material) => (
              <MaterialCard
                key={material.id}
                material={material}
                href={detailRoute(material.id)}
                viewedAt={isTeaching ? null : viewedMap?.get(material.id)?.viewedAt}
                renderBookmark={
                  isTeaching
                    ? undefined
                    : ({ courseId, materialId, className }) => (
                        <BookmarkToggleButton
                          courseId={courseId}
                          entityType="Material"
                          entityId={materialId}
                          className={className}
                        />
                      )
                }
                showStatus={isTeaching}
                allowDelete={isTeaching && activeTab === "mine"}
                onDelete={onDeleteMaterial}
                isDeletePending={isDeletePending}
              />
            ))}
          </div>

          {hasNextPage && (
            <div ref={cursorRef} className="flex justify-center py-6">
              <Button
                variant="outline"
                onClick={() => void fetchNextPage()}
                disabled={isFetchingNextPage}
              >
                {isFetchingNextPage ? (
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
        </>
      )}
    </div>
  );
}
