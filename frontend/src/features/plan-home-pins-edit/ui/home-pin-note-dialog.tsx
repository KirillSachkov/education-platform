"use client";

import type { HomePinListItemDto } from "@/entities/plan-pinned-material";
import { FormDialog } from "@/shared/ui/components/form-dialog";
import { Textarea } from "@/shared/ui/kit/textarea";
import { Label } from "@/shared/ui/kit/label";
import { type FormEvent, useState } from "react";
import { useUpdateHomePinNote } from "../model/use-home-pins-mutations";

const NOTE_MAX = 280;

type Props = {
  planId: string;
  pin: HomePinListItemDto | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

/**
 * Диалог редактирования заметки закрепа. Заметка показывается студентам под карточкой
 * на /home. Пустая строка → `null` (очищает заметку на бэке). Внутреннее состояние
 * пересоздаётся на каждый pin через `key`, чтобы избежать `setState` в эффекте
 * (React Compiler).
 */
export function HomePinNoteDialog({ planId, pin, open, onOpenChange }: Props) {
  if (!pin) return null;
  return (
    <HomePinNoteDialogInner
      key={pin.pinId}
      planId={planId}
      pin={pin}
      open={open}
      onOpenChange={onOpenChange}
    />
  );
}

function HomePinNoteDialogInner({
  planId,
  pin,
  open,
  onOpenChange,
}: Props & { pin: HomePinListItemDto }) {
  const [note, setNote] = useState(pin.note ?? "");
  const updateNote = useUpdateHomePinNote(planId);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = note.trim();
    updateNote.mutate(
      { pinId: pin.pinId, note: trimmed.length > 0 ? trimmed : null },
      { onSuccess: () => onOpenChange(false) },
    );
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Заметка к закрепу"
      description={`Короткий контекст под карточкой «${pin.title.trim() || "материала"}». Видна ученикам на главной.`}
      onSubmit={handleSubmit}
      isPending={updateNote.isPending}
    >
      <div className="space-y-1.5">
        <Label htmlFor="home-pin-note">Заметка</Label>
        <Textarea
          id="home-pin-note"
          value={note}
          maxLength={NOTE_MAX}
          rows={3}
          placeholder="Например: начни отсюда — это вводный материал"
          onChange={(event) => setNote(event.target.value)}
        />
        <p className="text-right text-xs text-muted-foreground tabular-nums">
          {note.length}/{NOTE_MAX}
        </p>
      </div>
    </FormDialog>
  );
}
