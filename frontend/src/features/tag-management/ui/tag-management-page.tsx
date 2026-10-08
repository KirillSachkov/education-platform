"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, Plus, Tags, Loader2 } from "lucide-react";
import { useDebouncedCallback } from "use-debounce";
import { type TagKindFilter, tagsQueryOptions } from "@/entities/tag";
import { routes } from "@/shared/config/routes";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Input } from "@/shared/ui/kit/input";
import { Tabs, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { CreateTagDialog } from "./create-tag-dialog";
import { DeleteTagDialog } from "./delete-tag-dialog";
import { RenameTagDialog } from "./rename-tag-dialog";
import { TagCard } from "./tag-card";

const PAGE_SIZE = 20;

const kindTabs: { value: TagKindFilter; label: string }[] = [
  { value: "all", label: "Все" },
  { value: "canon", label: "Каноничные" },
  { value: "alias", label: "Алиасы" },
];

function getTagsCountLabel(count: number) {
  const mod10 = count % 10;
  const mod100 = count % 100;

  if (mod10 === 1 && mod100 !== 11) {
    return "тег";
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return "тега";
  }

  return "тегов";
}

export function TagManagementPage() {
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [kindFilter, setKindFilter] = useState<TagKindFilter>("all");
  // Stack of cursors: null = first page, element [i] is the cursor that OPENED page i+1.
  // Current page index = cursorStack.length - 1.
  const [cursorStack, setCursorStack] = useState<(string | null)[]>([null]);
  const [createOpen, setCreateOpen] = useState(false);

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setDebouncedSearch(value);
  }, 350);

  const currentCursor = cursorStack[cursorStack.length - 1];

  const resetPagination = () => setCursorStack([null]);

  const { data, isLoading, isFetching, isError, error, refetch } = useQuery(
    tagsQueryOptions.list({
      cursor: currentCursor,
      limit: PAGE_SIZE,
      search: debouncedSearch.trim() || undefined,
      kind: kindFilter !== "all" ? kindFilter : undefined,
    }),
  );

  const result = data?.result;
  const tags = result?.items ?? [];
  const totalCount = result?.totalCount ?? 0;
  const nextCursor = result?.nextCursor ?? null;

  const currentPage = cursorStack.length;
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / PAGE_SIZE);
  const canGoPrev = cursorStack.length > 1;
  const canGoNext = !!nextCursor;

  return (
    <div className="mx-auto max-w-7xl p-4 sm:p-6">
      <div className="flex flex-wrap items-center gap-3 justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold">Управление тегами</h1>
          <p className="text-sm text-muted-foreground">
            Создавайте, находите, объединяйте и удаляйте теги.
          </p>
        </div>

        <Button
          className="bg-gradient-primary text-primary-foreground border-0 hover:opacity-90"
          onClick={() => setCreateOpen(true)}
        >
          <Plus size={15} />
          Добавить тег
        </Button>
      </div>

      <div className="mb-5 flex flex-col gap-3">
        <div className="relative w-full">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <Input
            value={search}
            onChange={(e) => {
              const value = e.target.value;
              setSearch(value);
              resetPagination();
              debouncedSetSearch(value);
            }}
            placeholder="Поиск по названию тега..."
            className="pl-9"
          />
        </div>

        <Tabs
          value={kindFilter}
          onValueChange={(value) => {
            setKindFilter(value as TagKindFilter);
            resetPagination();
          }}
        >
          <TabsList>
            {kindTabs.map((tab) => (
              <TabsTrigger key={tab.value} value={tab.value}>
                {tab.label}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
      </div>

      <div className="flex items-center gap-2 mb-4">
        <Badge variant="secondary">
          {isLoading ? "..." : totalCount} {getTagsCountLabel(totalCount)}
        </Badge>
        {isFetching && !isLoading && (
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
            <Loader2 className="size-3.5 animate-spin" />
            Обновление...
          </span>
        )}
      </div>

      {isError && (
        <Card className="p-6">
          <ErrorCard
            error={error as Error}
            onRetry={() => {
              void refetch();
            }}
          />
        </Card>
      )}

      {isLoading && (
        <div className="flex items-center justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!isLoading && !isError && tags.length === 0 && (
        <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
          <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
            <Tags size={22} className="text-muted-foreground" />
          </div>
          <p className="text-sm font-medium mb-1">Теги не найдены</p>
          <p className="text-sm text-muted-foreground">
            Попробуйте изменить фильтры или добавьте новый тег
          </p>
        </div>
      )}

      {!isLoading && !isError && tags.length > 0 && (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
          {tags.map((tag) => (
            <TagCard
              key={tag.id}
              tag={tag}
              href={routes.authorTag(tag.id)}
              actionSlot={
                <>
                  <RenameTagDialog tagId={tag.id} tagTitle={tag.title} variant="icon" />
                  <DeleteTagDialog tagId={tag.id} tagTitle={tag.title} variant="icon" />
                </>
              }
            />
          ))}
        </div>
      )}

      {!isLoading && !isError && (canGoPrev || canGoNext) && (
        <div className="flex items-center justify-between mt-5">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setCursorStack((s) => (s.length > 1 ? s.slice(0, -1) : s))}
            disabled={!canGoPrev || isFetching}
          >
            Назад
          </Button>

          <p className="text-sm text-muted-foreground">
            Страница {currentPage}
            {totalPages > 0 ? ` из ${totalPages}` : ""}
          </p>

          <Button
            variant="outline"
            size="sm"
            onClick={() => nextCursor && setCursorStack((s) => [...s, nextCursor])}
            disabled={!canGoNext || isFetching}
          >
            Далее
          </Button>
        </div>
      )}

      <CreateTagDialog open={createOpen} onOpenChange={setCreateOpen} />
    </div>
  );
}
