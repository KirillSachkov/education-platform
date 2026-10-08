"use client";

import { FormDialog } from "@/shared/ui/components/form-dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useState } from "react";
import { useCreateMockInterview } from "../model/use-create-mock-interview";

interface CreateMockInterviewDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Получает id созданного DRAFT-мок-собеса (раскрыть редактор). */
  onCreated: (interviewId: string) => void;
}

/** Диалог «Создать мок-собес»: только название → POST DRAFT → onCreated(id). */
export function CreateMockInterviewDialog({
  open,
  onOpenChange,
  onCreated,
}: CreateMockInterviewDialogProps) {
  const [title, setTitle] = useState("");
  const createMutation = useCreateMockInterview();

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = title.trim();
    if (trimmed.length === 0) return;
    createMutation.mutate(trimmed, {
      onSuccess: (interviewId) => {
        onOpenChange(false);
        setTitle("");
        onCreated(interviewId);
      },
    });
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={(nextOpen) => {
        onOpenChange(nextOpen);
        if (!nextOpen) setTitle("");
      }}
      title="Создать мок-собес"
      description="Мок-собес создаётся черновиком — вопросы соберёте в редакторе"
      onSubmit={handleSubmit}
      isPending={createMutation.isPending}
      submitLabel="Создать"
      submitDisabled={title.trim().length === 0}
    >
      <div className="space-y-1.5">
        <Label htmlFor="create-mock-title">Название</Label>
        <Input
          id="create-mock-title"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          maxLength={200}
          placeholder="Например: Найм в IT — джуниор-бэкенд"
          autoFocus
        />
      </div>
    </FormDialog>
  );
}
