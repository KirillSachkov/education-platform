"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  ArrowLeft,
  ArrowRightLeft,
  Check,
  CheckSquare,
  Loader2,
  Search,
  Unlink,
  X,
} from "lucide-react";
import { useDebouncedCallback } from "use-debounce";
import { type TagDto, tagsQueryOptions } from "@/entities/tag";
import { ErrorType, isEnvelopeError } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { NotFoundFallback } from "@/shared/ui/components/not-found-fallback";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { ErrorCard } from "@/shared/ui/kit/error-card";
import { Input } from "@/shared/ui/kit/input";
import { useMergeTags } from "../model/use-merge-tags";
import { useRemoveAliases } from "../model/use-remove-aliases";
import { DeleteTagDialog } from "./delete-tag-dialog";
import { RenameTagDialog } from "./rename-tag-dialog";
import { TagStatusBadge } from "./tag-status-badge";

interface TagDetailPageProps {
  tagId: string;
}

const ALIASES_PAGE_SIZE = 20;

type SelectionTone = "primary" | "destructive";

const toneStyles: Record<
  SelectionTone,
  {
    chip: string;
    cardSelected: string;
    indicatorSelected: string;
    indicatorIdle: string;
  }
> = {
  primary: {
    chip: "border-primary/45 bg-card/90 text-foreground",
    cardSelected:
      "border-primary/60 bg-background/30 shadow-[0_0_0_1px_hsl(var(--primary)/0.14)]",
    indicatorSelected: "border-primary/40 bg-primary text-primary-foreground",
    indicatorIdle: "border-border/70 bg-background/40",
  },
  destructive: {
    chip: "border-destructive/45 bg-card/90 text-foreground",
    cardSelected:
      "border-destructive/60 bg-background/30 shadow-[0_0_0_1px_hsl(var(--destructive)/0.14)]",
    indicatorSelected:
      "border-destructive/40 bg-destructive text-destructive-foreground",
    indicatorIdle: "border-border/70 bg-background/40",
  },
};

function getAliasesCountLabel(count: number) {
  const mod10 = count % 10;
  const mod100 = count % 100;

  if (mod10 === 1 && mod100 !== 11) {
    return "алиас";
  }

  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
    return "алиаса";
  }

  return "алиасов";
}

function SelectedTagChip({
  tag,
  tone,
  onRemove,
}: {
  tag: TagDto;
  tone: SelectionTone;
  onRemove: (tag: TagDto) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onRemove(tag)}
      className={cn(
        "inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm font-medium transition-colors hover:bg-background/95",
        toneStyles[tone].chip,
      )}
    >
      <span className="max-w-44 truncate">{tag.title}</span>
      <X className="size-3.5" />
    </button>
  );
}

function SelectableTagCard({
  tag,
  selected,
  tone,
  onToggle,
}: {
  tag: TagDto;
  selected: boolean;
  tone: SelectionTone;
  onToggle: (tag: TagDto) => void;
}) {
  return (
    <button
      type="button"
      onClick={() => onToggle(tag)}
      className={cn(
        "flex h-full w-full flex-col rounded-2xl border text-left transition-colors",
        "min-h-[104px] p-3",
        selected
          ? toneStyles[tone].cardSelected
          : "border-border/70 bg-background/30 hover:border-primary/25 hover:bg-muted/25",
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <TagStatusBadge
          kind={tag.kind}
          className="h-6 rounded-full px-2.5 text-[11px] font-medium"
        />
        <span
          className={cn(
            "inline-flex shrink-0 items-center justify-center rounded-full border transition-colors",
            "size-5",
            selected
              ? toneStyles[tone].indicatorSelected
              : toneStyles[tone].indicatorIdle,
          )}
        >
          {selected && <Check className="size-3" />}
        </span>
      </div>

      <div className="mt-3 text-[15px] font-semibold leading-tight">
        {tag.title}
      </div>
    </button>
  );
}

function SectionHeading({
  icon,
  title,
  description,
  badge,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
  badge?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 border-b border-border/60 pb-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="min-w-0">
        <div className="flex items-center gap-2 text-base font-semibold">
          <span className="text-primary">{icon}</span>
          <span>{title}</span>
        </div>
        <p className="mt-1 text-sm text-muted-foreground">{description}</p>
      </div>
      {badge}
    </div>
  );
}

function EmptySelectionState({
  title,
  description,
}: {
  title: string;
  description: string;
}) {
  return (
    <div className="flex min-h-40 flex-col items-center justify-center rounded-2xl border border-dashed border-border/70 bg-background/20 px-6 py-10 text-center">
      <div className="mb-3 flex size-11 items-center justify-center rounded-xl border border-border/70 bg-muted/30">
        <CheckSquare className="size-5 text-muted-foreground" />
      </div>
      <p className="text-sm font-medium">{title}</p>
      <p className="mt-1 max-w-sm text-sm text-muted-foreground">
        {description}
      </p>
    </div>
  );
}

export function TagDetailPage({ tagId }: TagDetailPageProps) {
  const [candidateSearch, setCandidateSearch] = useState("");
  const [debouncedCandidateSearch, setDebouncedCandidateSearch] = useState("");
  const [aliasSearch, setAliasSearch] = useState("");
  const [debouncedAliasSearch, setDebouncedAliasSearch] = useState("");
  const [selectedTags, setSelectedTags] = useState<TagDto[]>([]);
  const [selectedAliases, setSelectedAliases] = useState<TagDto[]>([]);
  const [aliasesPage, setAliasesPage] = useState(1);

  const debouncedSetSearch = useDebouncedCallback((value: string) => {
    setDebouncedCandidateSearch(value);
  }, 350);

  const debouncedSetAliasSearch = useDebouncedCallback((value: string) => {
    setDebouncedAliasSearch(value);
  }, 350);

  const {
    data: tagResponse,
    isLoading: isTagLoading,
    isError: isTagError,
    error: tagError,
    refetch: refetchTag,
  } = useQuery(tagsQueryOptions.byId(tagId));

  const tag = tagResponse?.result;

  const { mergeTags, isPending: isMerging } = useMergeTags();
  const { removeAliases, isPending: isRemovingAliases } = useRemoveAliases();

  const {
    data: mergeCandidates = [],
    isLoading: isCandidatesLoading,
    isFetching: isCandidatesFetching,
    isError: isCandidatesError,
    error: candidatesError,
    refetch: refetchCandidates,
  } = useQuery({
    ...tagsQueryOptions.mergeCandidates({
      search: debouncedCandidateSearch.trim() || undefined,
    }),
    enabled: tag?.kind === "canon",
  });

  const {
    data: aliasesResponse,
    isLoading: isAliasesLoading,
    isError: isAliasesError,
    error: aliasesError,
    refetch: refetchAliases,
  } = useQuery({
    ...tagsQueryOptions.aliasesPage(tagId, {
      page: aliasesPage,
      pageSize: ALIASES_PAGE_SIZE,
      search: debouncedAliasSearch.trim() || undefined,
    }),
    enabled: tag?.kind === "canon",
  });

  const availableCandidates = tag
    ? mergeCandidates.filter(
        (candidate) => candidate.id !== tag.id && candidate.kind === "canon",
      )
    : [];

  const aliasesResult = aliasesResponse?.result;
  const aliases = aliasesResult?.items ?? [];
  const aliasesTotalCount = aliasesResult?.totalCount ?? 0;
  const aliasesTotalPages = aliasesResult?.totalPages ?? 0;
  const canGoPrevAliases = aliasesPage > 1;
  const canGoNextAliases = aliasesPage < aliasesTotalPages;

  if (isTagLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (!tag && isTagError) {
    if (isEnvelopeError(tagError) && tagError.type === ErrorType.NOT_FOUND) {
      return (
        <NotFoundFallback
          message="Тег не найден"
          backHref={routes.authorTags}
          backLabel="К списку тегов"
        />
      );
    }

    return (
      <div className="mx-auto max-w-6xl p-6">
        <Card className="p-6">
          <ErrorCard
            error={tagError as Error}
            onRetry={() => {
              void refetchTag();
            }}
          />
        </Card>
      </div>
    );
  }

  if (!tag) {
    return (
      <NotFoundFallback
        message="Тег не найден"
        backHref={routes.authorTags}
        backLabel="К списку тегов"
      />
    );
  }

  const isAliasTag = tag.kind === "alias";
  const selectedCount = selectedTags.length;

  function isSelected(tagIdToCheck: string) {
    return selectedTags.some((selected) => selected.id === tagIdToCheck);
  }

  function toggleTag(candidate: TagDto) {
    setSelectedTags((prev) =>
      prev.some((selected) => selected.id === candidate.id)
        ? prev.filter((selected) => selected.id !== candidate.id)
        : [...prev, candidate],
    );
  }

  function isAliasSelected(aliasId: string) {
    return selectedAliases.some((selected) => selected.id === aliasId);
  }

  function toggleAlias(alias: TagDto) {
    setSelectedAliases((prev) =>
      prev.some((selected) => selected.id === alias.id)
        ? prev.filter((selected) => selected.id !== alias.id)
        : [...prev, alias],
    );
  }

  return (
    <div className="mx-auto max-w-7xl px-6 py-6">
      <div className="mb-5">
        <Button variant="ghost" size="sm" asChild className="w-fit">
          <Link href={routes.authorTags}>
            <ArrowLeft className="size-4" />
            К списку тегов
          </Link>
        </Button>
      </div>

      <div className="space-y-5">
        <Card className="border-border/70 bg-card/80 p-5">
          <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <TagStatusBadge
                  kind={tag.kind}
                  className="h-7 rounded-full px-2.5 text-[11px] font-medium"
                />
                {!isAliasTag && (
                  <Badge variant="secondary" className="rounded-full">
                    {aliasesTotalCount} {getAliasesCountLabel(aliasesTotalCount)}
                  </Badge>
                )}
              </div>

              <h1 className="mt-5 text-2xl font-semibold leading-tight tracking-[-0.02em] sm:text-3xl">
                {tag.title}
              </h1>
            </div>

            <div className="flex items-center gap-1 text-muted-foreground">
              <RenameTagDialog
                tagId={tag.id}
                tagTitle={tag.title}
                variant="icon"
              />
              <DeleteTagDialog
                tagId={tag.id}
                tagTitle={tag.title}
                variant="icon"
              />
            </div>
          </div>
        </Card>

        <Card className="border-border/70 bg-card/80 p-5">
            <SectionHeading
              icon={<Unlink className="size-4" />}
              title="Алиасы"
              description="Отмечайте алиасы карточками и массово отвязывайте их от текущего каноничного тега."
              badge={
                !isAliasTag ? (
                  <Badge variant="secondary" className="rounded-full">
                    {aliasesTotalCount} {getAliasesCountLabel(aliasesTotalCount)}
                  </Badge>
                ) : undefined
              }
            />

            {isAliasTag ? (
              <div className="mt-5 rounded-2xl border border-dashed border-border/70 bg-background/20 p-5 text-sm text-muted-foreground">
                Этот тег уже является алиасом. Отдельный список алиасов для него
                не ведётся.
              </div>
            ) : (
              <div className="mt-5 space-y-4">
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                  <Input
                    value={aliasSearch}
                    onChange={(e) => {
                      const value = e.target.value;
                      setAliasSearch(value);
                      setAliasesPage(1);
                      debouncedSetAliasSearch(value);
                    }}
                    placeholder="Поиск по алиасам..."
                    className="pl-9"
                  />
                </div>

                {selectedAliases.length > 0 && (
                  <div className="rounded-2xl border border-border/70 bg-background/25 p-4">
                    <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                      <div className="min-w-0">
                        <p className="text-sm font-medium">
                          Выбрано для отвязки
                        </p>
                        <div className="mt-2 flex flex-wrap gap-2">
                          {selectedAliases.map((selected) => (
                            <SelectedTagChip
                              key={selected.id}
                              tag={selected}
                              tone="destructive"
                              onRemove={toggleAlias}
                            />
                          ))}
                        </div>
                      </div>
                      <Button
                        variant="destructive"
                        disabled={
                          selectedAliases.length === 0 || isRemovingAliases
                        }
                        onClick={async () => {
                          try {
                            await removeAliases({
                              tagId: tag.id,
                              request: {
                                tagIds: selectedAliases.map((alias) => alias.id),
                              },
                            });
                            setSelectedAliases([]);
                          } catch {
                            // Error is already handled in mutation hook via toast.
                          }
                        }}
                      >
                        {isRemovingAliases && (
                          <Loader2 className="mr-2 size-4 animate-spin" />
                        )}
                        Отвязать выбранные
                      </Button>
                    </div>
                  </div>
                )}

                {isAliasesLoading && (
                  <div className="flex items-center gap-2 py-4 text-sm text-muted-foreground">
                    <Loader2 className="size-4 animate-spin" />
                    Загружаем алиасы...
                  </div>
                )}

                {!isAliasesLoading && isAliasesError && (
                  <ErrorCard
                    error={aliasesError as Error}
                    onRetry={() => {
                      void refetchAliases();
                    }}
                  />
                )}

                {!isAliasesLoading &&
                  !isAliasesError &&
                  aliasesTotalCount === 0 && (
                    <EmptySelectionState
                      title="Алиасы пока не добавлены"
                      description="После объединения или ручной привязки связанные теги появятся здесь."
                    />
                  )}

                {!isAliasesLoading &&
                  !isAliasesError &&
                  aliasesTotalCount > 0 &&
                  aliases.length === 0 && (
                    <EmptySelectionState
                      title="Ничего не найдено"
                      description="По этому запросу алиасы не найдены."
                    />
                  )}

                {!isAliasesLoading &&
                  !isAliasesError &&
                  aliases.length > 0 && (
                    <>
                      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-4">
                        {aliases.map((alias) => (
                          <SelectableTagCard
                            key={alias.id}
                            tag={alias}
                            selected={isAliasSelected(alias.id)}
                            tone="destructive"
                            onToggle={toggleAlias}
                          />
                        ))}
                      </div>

                      <div className="flex flex-col gap-3 border-t border-border/60 pt-4 sm:flex-row sm:items-center sm:justify-end">
                        {aliasesTotalPages > 1 && (
                          <div className="flex items-center gap-2">
                            <p className="text-xs text-muted-foreground">
                              Страница {aliasesPage} из {aliasesTotalPages}
                            </p>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() =>
                                setAliasesPage((prev) => Math.max(1, prev - 1))
                              }
                              disabled={!canGoPrevAliases || isAliasesLoading}
                            >
                              Назад
                            </Button>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => setAliasesPage((prev) => prev + 1)}
                              disabled={!canGoNextAliases || isAliasesLoading}
                            >
                              Далее
                            </Button>
                          </div>
                        )}
                      </div>
                    </>
                  )}
              </div>
            )}
          </Card>

        <Card className="border-border/70 bg-card/80 p-5">
            <SectionHeading
              icon={<ArrowRightLeft className="size-4" />}
              title="Слияние"
              description="Выбирайте каноничные теги и превращайте их в алиасы текущего тега без отдельной страницы."
            />

            {isAliasTag ? (
              <div className="mt-5 rounded-2xl border border-primary/20 bg-primary/5 p-5 text-sm text-muted-foreground">
                Этот тег уже является алиасом. Слияние доступно только для
                каноничных тегов.
              </div>
            ) : (
              <div className="mt-5 space-y-4">
                <div className="flex flex-col gap-3">
                  <div className="relative flex-1">
                    <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      value={candidateSearch}
                      onChange={(e) => {
                        const value = e.target.value;
                        setCandidateSearch(value);
                        debouncedSetSearch(value);
                      }}
                      placeholder="Поиск каноничных тегов для слияния..."
                      className="pl-9"
                    />
                  </div>
                </div>

                {selectedTags.length > 0 && (
                  <div className="rounded-2xl border border-border/70 bg-background/25 p-4">
                    <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                      <div className="min-w-0">
                        <p className="text-sm font-medium">
                          Выбрано для слияния
                        </p>
                        <div className="mt-2 flex flex-wrap gap-2">
                          {selectedTags.map((selected) => (
                            <SelectedTagChip
                              key={selected.id}
                              tag={selected}
                              tone="primary"
                              onRemove={toggleTag}
                            />
                          ))}
                        </div>
                      </div>
                      <Button
                        disabled={selectedCount === 0 || isMerging}
                        onClick={async () => {
                          try {
                            await mergeTags({
                              tagId: tag.id,
                              request: {
                                tagIds: selectedTags.map((selected) => selected.id),
                              },
                            });
                            setSelectedTags([]);
                          } catch {
                            // Error is already handled in mutation hook via toast.
                          }
                        }}
                      >
                        {isMerging && (
                          <Loader2 className="mr-2 size-4 animate-spin" />
                        )}
                        Слить выбранные
                      </Button>
                    </div>
                  </div>
                )}

                <div className="rounded-2xl border border-border/70 bg-background/20 p-3">
                  {isCandidatesLoading && (
                    <div className="flex items-center justify-center py-10">
                      <Loader2 className="size-5 animate-spin text-muted-foreground" />
                    </div>
                  )}

                  {!isCandidatesLoading && isCandidatesError && (
                    <ErrorCard
                      error={candidatesError as Error}
                      onRetry={() => {
                        void refetchCandidates();
                      }}
                    />
                  )}

                  {!isCandidatesLoading &&
                    !isCandidatesError &&
                    availableCandidates.length === 0 && (
                      <EmptySelectionState
                        title="Нет доступных тегов"
                        description="Попробуйте изменить поисковый запрос или создайте новые каноничные теги."
                      />
                    )}

                  {!isCandidatesLoading &&
                    !isCandidatesError &&
                    availableCandidates.length > 0 && (
                      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 2xl:grid-cols-4">
                        {availableCandidates.map((candidate) => (
                          <SelectableTagCard
                            key={candidate.id}
                            tag={candidate}
                            selected={isSelected(candidate.id)}
                            tone="primary"
                            onToggle={toggleTag}
                          />
                        ))}
                      </div>
                    )}
                </div>

                <div className="flex items-center justify-end gap-3 border-t border-border/60 pt-4">
                  {isCandidatesFetching && (
                    <p className="text-xs text-muted-foreground">
                      Обновляем список...
                    </p>
                  )}
                </div>
              </div>
            )}
          </Card>
      </div>
    </div>
  );
}
