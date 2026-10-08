"use client";

import type { UpdateVideoChapterItem, VideoChapter } from "@/entities/video";
import { cn } from "@/shared/lib/css";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/shared/ui/kit/tooltip";
import { AlertCircle, Clock3, Loader2, Plus, Save, Sparkles, Trash2 } from "lucide-react";
import { useState, type KeyboardEvent, type MouseEvent } from "react";
import { toast } from "sonner";
import { formatVideoTime, parseVideoTime } from "../lib/video-processing-ui";

interface EditableTimecode {
  id: string;
  persistedId: string | null;
  startSeconds: number;
  startText: string;
  title: string;
  sortOrder: number;
}

interface TimecodesEditorProps {
  timecodes: VideoChapter[];
  currentTime?: number;
  isSaving?: boolean;
  isGenerating?: boolean;
  canGenerate?: boolean;
  generateTooltip?: string;
  onGenerate?: () => void;
  onSeekChapter?: (seconds: number) => Promise<void> | void;
  onSave: (timecodes: UpdateVideoChapterItem[]) => Promise<void>;
}

function toEditable(timecode: VideoChapter): EditableTimecode {
  return {
    id: timecode.id,
    persistedId: timecode.id,
    startSeconds: timecode.startSeconds,
    startText: formatVideoTime(timecode.startSeconds),
    title: timecode.title,
    sortOrder: timecode.sortOrder,
  };
}

function getActiveChapterId(timecodes: EditableTimecode[], currentTime: number): string | null {
  const ordered = [...timecodes].sort((a, b) => a.startSeconds - b.startSeconds);
  let active: EditableTimecode | null = null;

  for (const chapter of ordered) {
    if (chapter.startSeconds <= currentTime) {
      active = chapter;
      continue;
    }
    break;
  }

  return active?.id ?? null;
}

export function TimecodesEditor({
  timecodes,
  currentTime = 0,
  isSaving = false,
  isGenerating = false,
  canGenerate = true,
  generateTooltip = "Сгенерировать главы",
  onGenerate,
  onSeekChapter,
  onSave,
}: TimecodesEditorProps) {
  const [items, setItems] = useState<EditableTimecode[]>(() =>
    [...timecodes].sort((a, b) => a.sortOrder - b.sortOrder).map(toEditable),
  );
  const [dirty, setDirty] = useState(false);

  const activeChapterId = getActiveChapterId(items, currentTime);
  const orderedItems = [...items].sort((a, b) => a.startSeconds - b.startSeconds);

  const updateItem = (id: string, patch: Partial<EditableTimecode>) => {
    setItems((current) => current.map((item) => (item.id === id ? { ...item, ...patch } : item)));
    setDirty(true);
  };

  const handleStartChange = (id: string, value: string) => {
    const parsed = parseVideoTime(value);
    updateItem(id, {
      startText: value,
      ...(parsed !== null ? { startSeconds: parsed } : {}),
    });
  };

  const handleAddChapter = () => {
    const baseSeconds = Math.max(0, Math.floor(currentTime));
    const existingStarts = new Set(items.map((item) => item.startSeconds));
    const lastStart = Math.max(-1, ...items.map((item) => item.startSeconds));

    let startSeconds = baseSeconds;
    if (existingStarts.has(startSeconds)) {
      startSeconds = lastStart >= 0 ? lastStart + 60 : 0;
    }

    while (existingStarts.has(startSeconds)) {
      startSeconds += 1;
    }

    setItems((current) => [
      ...current,
      {
        id: `draft-${crypto.randomUUID()}`,
        persistedId: null,
        startSeconds,
        startText: formatVideoTime(startSeconds),
        title: "",
        sortOrder: current.length + 1,
      },
    ]);
    setDirty(true);
  };

  const handleDeleteChapter = (id: string) => {
    setItems((current) => current.filter((item) => item.id !== id));
    setDirty(true);
  };

  const seekToChapter = async (chapter: EditableTimecode) => {
    await onSeekChapter?.(chapter.startSeconds);
  };

  const preventRowSeek = (event: MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  };

  const handleRowKeyDown = (event: KeyboardEvent<HTMLDivElement>, chapter: EditableTimecode) => {
    const tag = (event.target as HTMLElement).tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") return;
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      void seekToChapter(chapter);
    }
  };

  const buildPayload = (): UpdateVideoChapterItem[] | null => {
    const invalidItem = items.find(
      (item) => parseVideoTime(item.startText) === null || !item.title.trim(),
    );

    if (invalidItem) {
      toast.error("Проверьте время и название глав");
      return null;
    }

    const ordered = [...items]
      .map((item) => ({
        id: item.persistedId,
        startSeconds: parseVideoTime(item.startText) ?? item.startSeconds,
        title: item.title.trim(),
        sortOrder: item.sortOrder,
      }))
      .sort((a, b) => a.startSeconds - b.startSeconds)
      .map((item, index) => ({
        ...item,
        sortOrder: index + 1,
      }));

    for (let index = 1; index < ordered.length; index += 1) {
      if (ordered[index].startSeconds <= ordered[index - 1].startSeconds) {
        toast.error("Главы должны идти по возрастанию времени");
        return null;
      }
    }

    return ordered;
  };

  const handleSave = async () => {
    const payload = buildPayload();
    if (!payload) return;
    await onSave(payload);
    setDirty(false);
  };

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-medium tracking-tight">Главы видео</h3>
            {items.length > 0 && (
              <Badge
                variant="secondary"
                className="h-5 rounded-md bg-muted px-1.5 text-[10px] font-semibold"
              >
                {items.length}
              </Badge>
            )}
            {dirty && (
              <Badge
                variant="outline"
                className="h-5 rounded-md border-border/60 px-1.5 text-[10px] text-muted-foreground"
              >
                Есть изменения
              </Badge>
            )}
          </div>
          <p className="mt-1 text-xs text-muted-foreground">
            Клик по главе перематывает видео в верхнем блоке к нужному моменту.
          </p>
        </div>

        <div className="flex items-center gap-1.5">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                disabled={isSaving || isGenerating}
                onClick={handleAddChapter}
                className="min-touch text-muted-foreground"
              >
                <Plus size={14} />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Добавить главу</TooltipContent>
          </Tooltip>

          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                disabled={!canGenerate || isGenerating}
                onClick={onGenerate}
                className="min-touch text-muted-foreground"
              >
                {isGenerating ? (
                  <Loader2 size={14} className="animate-spin" />
                ) : (
                  <Sparkles size={14} />
                )}
              </Button>
            </TooltipTrigger>
            <TooltipContent>{generateTooltip}</TooltipContent>
          </Tooltip>

          <Button
            type="button"
            size="sm"
            disabled={!dirty || isSaving || isGenerating}
            onClick={() => void handleSave()}
            className="min-touch h-8 min-w-[8.5rem]"
          >
            {isSaving ? <Loader2 size={14} className="animate-spin" /> : <Save size={14} />}
            Сохранить главы
          </Button>
        </div>
      </div>

      {items.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-2 px-4 py-8 text-center">
          {isGenerating ? (
            <Loader2 className="size-5 animate-spin text-primary" />
          ) : (
            <AlertCircle className="size-5 text-muted-foreground" />
          )}
          <p className="text-sm font-medium">
            {isGenerating ? "Генерируем главы" : "Глав пока нет"}
          </p>
          <p className="max-w-md text-xs text-muted-foreground">
            Добавьте главы вручную или запустите генерацию, чтобы получить черновой список
            тайм-кодов и затем уточнить его.
          </p>
          {!isGenerating && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleAddChapter}
              className="mt-1 h-8"
            >
              <Plus size={14} />
              Добавить главу
            </Button>
          )}
        </div>
      ) : (
        <div className="max-h-[34rem] overflow-auto">
          <div className="space-y-2">
            {orderedItems.map((item, index) => {
              const active = item.id === activeChapterId;
              return (
                <div
                  key={item.id}
                  className={cn(
                    "relative rounded-lg border bg-card/35 px-2.5 py-2 transition-colors",
                    active
                      ? "border-primary/25 bg-primary/[0.045]"
                      : "border-border/60 hover:border-border",
                  )}
                >
                  <span
                    className={cn(
                      "absolute inset-y-2 left-0 w-0.5 rounded-full bg-transparent transition-colors",
                      active && "bg-primary",
                    )}
                  />

                  <div className="pl-1.5">
                    <div
                      role="button"
                      tabIndex={0}
                      onClick={() => void seekToChapter(item)}
                      onKeyDown={(event) => handleRowKeyDown(event, item)}
                      className={cn(
                        "grid items-end gap-x-3 gap-y-1.5 rounded-md text-left transition-colors lg:grid-cols-[8rem_minmax(0,1fr)_2.25rem]",
                        active ? "text-foreground" : "text-foreground/95",
                      )}
                      aria-label={`Перейти к главе ${index + 1} на ${formatVideoTime(item.startSeconds)}`}
                    >
                      <div className="space-y-1" onClick={preventRowSeek}>
                        <label className="flex items-center gap-1 text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                          <Clock3 size={11} />
                          Старт
                        </label>
                        <Input
                          value={item.startText}
                          onChange={(event) => handleStartChange(item.id, event.target.value)}
                          placeholder="0:00"
                          disabled={isSaving || isGenerating}
                          aria-label={`Время начала главы ${index + 1}`}
                          className="h-9 rounded-lg border-border/60 bg-background/50 px-2.5 font-mono tabular-nums"
                        />
                      </div>

                      <div className="space-y-1" onClick={preventRowSeek}>
                        <label className="text-[10px] uppercase tracking-[0.12em] text-muted-foreground">
                          Название
                        </label>
                        <Input
                          value={item.title}
                          onChange={(event) => updateItem(item.id, { title: event.target.value })}
                          disabled={isSaving || isGenerating}
                          placeholder="Название главы"
                          aria-label={`Название главы ${index + 1}`}
                          className="h-9 rounded-lg border-border/60 bg-background/50 px-2.5"
                        />
                      </div>

                      <div className="flex h-9 items-center justify-end" onClick={preventRowSeek}>
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-sm"
                          disabled={isSaving || isGenerating}
                          onClick={() => handleDeleteChapter(item.id)}
                          className="min-touch h-8 w-8 rounded-lg text-muted-foreground"
                          aria-label={`Удалить главу ${index + 1}`}
                        >
                          <Trash2 size={14} />
                        </Button>
                      </div>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
