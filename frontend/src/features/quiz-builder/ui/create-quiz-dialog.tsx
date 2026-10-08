"use client";

import { QUIZ_TITLE_MAX_LENGTH } from "@/entities/quiz";
import { FormDialog } from "@/shared/ui/components/form-dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useState } from "react";
import { useCreateQuiz } from "../model/use-create-quiz";

interface CreateQuizDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Получает id созданного DRAFT-квиза (раскрыть редактор / автопривязать). */
  onCreated: (quizId: string) => void;
}

/** Диалог «Создать квиз»: только название → POST DRAFT → onCreated(id). */
export function CreateQuizDialog({ open, onOpenChange, onCreated }: CreateQuizDialogProps) {
  const [title, setTitle] = useState("");
  const createMutation = useCreateQuiz();

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = title.trim();
    if (trimmed.length === 0) return;
    createMutation.mutate(trimmed, {
      onSuccess: (quizId) => {
        onOpenChange(false);
        setTitle("");
        onCreated(quizId);
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
      title="Создать тест"
      description="Тест создаётся черновиком — вопросы добавите в редакторе"
      onSubmit={handleSubmit}
      isPending={createMutation.isPending}
      submitLabel="Создать"
      submitDisabled={title.trim().length === 0}
    >
      <div className="space-y-1.5">
        <Label htmlFor="create-quiz-title">Название</Label>
        <Input
          id="create-quiz-title"
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          maxLength={QUIZ_TITLE_MAX_LENGTH}
          placeholder="Например: Проверь себя по теме урока"
          autoFocus
        />
      </div>
    </FormDialog>
  );
}
