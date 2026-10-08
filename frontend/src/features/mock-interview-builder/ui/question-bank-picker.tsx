"use client";

import { type QuestionBankItem, mockInterviewsQueryOptions } from "@/entities/mock-interview";
import { trainerTopicsQueryOptions } from "@/entities/trainer-topic";
import { trainerTracksQueryOptions } from "@/entities/trainer-track";
import { getErrorMessage } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";

/** Ключ ссылки на вопрос — для дедупа выбранных (#623 — по questionId). */
export function questionRefKey(questionId: string): string {
  return questionId;
}

/** Сентинел «все треки/темы» для Radix Select (пустые value запрещены). */
const ALL_VALUE = "__all__";

interface QuestionBankPickerProps {
  /** Ключи (questionId) уже добавленных вопросов — для отметки «в наборе». */
  selectedKeys: Set<string>;
  /** Добавить вопрос в курированный набор. */
  onAdd: (item: QuestionBankItem) => void;
  disabled?: boolean;
}

/**
 * Пикер банка вопросов (#585): браузер доступных вопросов с фильтром по треку
 * (сегменты) + теме (select) + текстовым поиском по стему. Каждая строка —
 * «Добавить»; уже добавленные показаны как «В наборе». Mobile-first, ≥44px.
 */
export function QuestionBankPicker({ selectedKeys, onAdd, disabled }: QuestionBankPickerProps) {
  const [trackId, setTrackId] = useState<string | null>(null);
  const [topicId, setTopicId] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  const tracksQuery = useQuery(trainerTracksQueryOptions.tracksOptions());
  const topicsQuery = useQuery(
    trainerTopicsQueryOptions.topicsOptions(trackId ? { trackId } : {}),
  );
  const bankQuery = useQuery(
    mockInterviewsQueryOptions.questionBankOptions({
      trackId: trackId ?? undefined,
      topicId: topicId ?? undefined,
    }),
  );

  const tracks = tracksQuery.data ?? [];
  const topics = topicsQuery.data ?? [];

  const normalizedSearch = search.trim().toLowerCase();
  const items = (bankQuery.data ?? []).filter(
    (item) => normalizedSearch === "" || item.text.toLowerCase().includes(normalizedSearch),
  );

  const handleTrackChange = (value: string) => {
    setTrackId(value === ALL_VALUE ? null : value);
    setTopicId(null); // тема сбрасывается — она зависит от трека.
  };

  return (
    <div className="space-y-3">
      {/* Сегменты треков */}
      <div className="flex flex-wrap gap-1.5">
        <TrackChip
          label="Все треки"
          active={trackId === null}
          onClick={() => handleTrackChange(ALL_VALUE)}
          disabled={disabled}
        />
        {tracks.map((track) => (
          <TrackChip
            key={track.id}
            label={track.title}
            active={trackId === track.id}
            onClick={() => handleTrackChange(track.id)}
            disabled={disabled}
          />
        ))}
      </div>

      {/* Тема + поиск */}
      <div className="grid gap-2 sm:grid-cols-[minmax(0,260px)_minmax(0,1fr)]">
        <div className="space-y-1">
          <Label htmlFor="picker-topic" className="text-xs">
            Тема
          </Label>
          <Select
            value={topicId ?? ALL_VALUE}
            onValueChange={(value) => setTopicId(value === ALL_VALUE ? null : value)}
            disabled={disabled || topicsQuery.isLoading}
          >
            <SelectTrigger id="picker-topic" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_VALUE}>Все темы</SelectItem>
              {topics.map((topic) => (
                <SelectItem key={topic.id} value={topic.id}>
                  {topic.title}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <Label htmlFor="picker-search" className="text-xs">
            Поиск по тексту вопроса
          </Label>
          <div className="relative">
            <Icons.search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              id="picker-search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Например: async"
              disabled={disabled}
              className="pl-9"
            />
          </div>
        </div>
      </div>

      {/* Результаты */}
      <QuestionBankResults
        items={items}
        isLoading={bankQuery.isLoading}
        error={bankQuery.error}
        onRetry={() => bankQuery.refetch()}
        selectedKeys={selectedKeys}
        onAdd={onAdd}
        disabled={disabled}
        hasSearch={normalizedSearch !== ""}
      />
    </div>
  );
}

function TrackChip({
  label,
  active,
  onClick,
  disabled,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      className={cn(
        "min-h-[36px] rounded-full border px-3 text-xs font-medium transition-colors",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
        "disabled:cursor-not-allowed disabled:opacity-50",
        active
          ? "border-primary bg-primary/10 text-primary"
          : "border-border/60 bg-card/50 text-muted-foreground hover:bg-accent/40",
      )}
    >
      {label}
    </button>
  );
}

function QuestionBankResults({
  items,
  isLoading,
  error,
  onRetry,
  selectedKeys,
  onAdd,
  disabled,
  hasSearch,
}: {
  items: QuestionBankItem[];
  isLoading: boolean;
  error: unknown;
  onRetry: () => void;
  selectedKeys: Set<string>;
  onAdd: (item: QuestionBankItem) => void;
  disabled?: boolean;
  hasSearch: boolean;
}) {
  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive">
        <span>{getErrorMessage(error, "Не удалось загрузить банк вопросов")}</span>
        <Button type="button" variant="outline" size="sm" onClick={onRetry}>
          <Icons.refresh className="size-3.5" />
          Повторить
        </Button>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <p className="rounded-lg border border-dashed border-border/70 bg-card/30 p-4 text-sm text-muted-foreground">
        {hasSearch
          ? "Ничего не нашлось по запросу — измените фильтры или поиск."
          : "Нет доступных вопросов под выбранные фильтры. Создайте банки с вопросами у опубликованных тем."}
      </p>
    );
  }

  return (
    <ul className="max-h-[28rem] space-y-2 overflow-y-auto pr-1">
      {items.map((item) => {
        const added = selectedKeys.has(questionRefKey(item.questionId));
        return (
          <li
            key={questionRefKey(item.questionId)}
            className="flex items-start gap-3 rounded-xl border border-border/60 bg-card/50 p-3"
          >
            <div className="min-w-0 flex-1 space-y-1">
              <p className="text-sm leading-snug">{item.text}</p>
              <div className="flex flex-wrap items-center gap-1.5 text-[11px] text-muted-foreground">
                <span className="rounded bg-muted px-1.5 py-0.5">{item.topicTitle}</span>
                <span className="rounded bg-muted px-1.5 py-0.5">{item.trackTitle}</span>
                {item.difficulty && (
                  <span className="rounded bg-muted px-1.5 py-0.5">{item.difficulty}</span>
                )}
                <span className="rounded bg-muted px-1.5 py-0.5">{item.type}</span>
              </div>
            </div>
            <Button
              type="button"
              variant={added ? "ghost" : "outline"}
              size="sm"
              className="shrink-0"
              onClick={() => onAdd(item)}
              disabled={disabled || added}
            >
              {added ? (
                <>
                  <Icons.check className="size-3.5" />В наборе
                </>
              ) : (
                <>
                  <Icons.add className="size-3.5" />
                  Добавить
                </>
              )}
            </Button>
          </li>
        );
      })}
    </ul>
  );
}
