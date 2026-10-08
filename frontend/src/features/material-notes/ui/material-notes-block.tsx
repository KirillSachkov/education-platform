"use client";

import { MATERIAL_NOTE_MAX_LENGTH, materialNoteQueryOptions } from "@/entities/material-note";
import { useDebouncedValue } from "@/shared/hooks";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useSaveMaterialNote } from "../model/use-save-material-note";

const AUTOSAVE_DELAY_MS = 800;

interface MaterialNotesBlockProps {
  materialId: string;
  className?: string;
}

/**
 * Свёрнутый блок «Мои заметки» под контентом материала (только для auth-юзеров).
 * Заметка видна только владельцу. Автосохранение: дебаунс ~800мс после остановки
 * ввода + немедленный flush на blur; пока мутация в полёте, эффект ждёт её
 * завершения и досохраняет хвост, если текст успел измениться. Issue #465.
 */
export function MaterialNotesBlock({ materialId, className }: MaterialNotesBlockProps) {
  const { data: note, isLoading } = useQuery(materialNoteQueryOptions.noteOptions(materialId));
  // null — пользователь ещё не редактировал в этой сессии, показываем серверное значение.
  const [draft, setDraft] = useState<string | null>(null);

  const savedContent = note?.content ?? "";
  const value = draft ?? savedContent;
  const debouncedDraft = useDebouncedValue(draft, AUTOSAVE_DELAY_MS);

  const { mutate: saveNote, isPending, isSuccess } = useSaveMaterialNote(materialId);

  useEffect(() => {
    if (debouncedDraft === null || isPending) return;
    if (debouncedDraft === savedContent) return;
    saveNote(debouncedDraft);
  }, [debouncedDraft, savedContent, isPending, saveNote]);

  const handleBlur = () => {
    if (draft === null || isPending || draft === savedContent) return;
    saveNote(draft);
  };

  const isSaved = isSuccess && !isPending && draft !== null && value === savedContent;

  return (
    <details className={cn("group", className)}>
      <summary className="flex cursor-pointer list-none items-center gap-2 text-sm font-semibold [&::-webkit-details-marker]:hidden">
        <Icons.note size={16} className="text-primary" />
        Мои заметки
        <Icons.chevronRight className="size-4 text-muted-foreground transition-transform group-open:rotate-90" />
      </summary>
      <div className="mt-3 space-y-1.5">
        <Textarea
          value={value}
          onChange={(event) => setDraft(event.target.value)}
          onBlur={handleBlur}
          disabled={isLoading}
          maxLength={MATERIAL_NOTE_MAX_LENGTH}
          placeholder="Личная заметка к материалу — видна только вам"
          className="min-h-32 bg-card/50"
        />
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span aria-live="polite" className="flex items-center gap-1">
            {isPending && (
              <>
                <Icons.loading className="size-3 animate-spin" />
                Сохранение…
              </>
            )}
            {isSaved && (
              <>
                <Icons.completed className="size-3 text-green" />
                Сохранено
              </>
            )}
          </span>
          <span>
            {value.length} / {MATERIAL_NOTE_MAX_LENGTH}
          </span>
        </div>
      </div>
    </details>
  );
}
