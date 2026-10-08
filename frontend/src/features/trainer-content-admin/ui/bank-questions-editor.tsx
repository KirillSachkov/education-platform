"use client";

import {
  trainerQuestionAdminQueryOptions,
  type TrainerQuestionAdmin,
} from "@/entities/trainer-question-admin";
import { getErrorMessage } from "@/shared/api";
import { TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useDeleteTrainerQuestion } from "../model/use-question-mutations";
import { QuestionFormDialog } from "./question-form-dialog";

const TYPE_LABELS: Record<string, string> = {
  SINGLE_CHOICE: "Один вариант",
  MULTI_CHOICE: "Несколько",
  EXACT_TEXT: "Точный текст",
  OPEN_TEXT: "Развёрнутый",
};

interface BankQuestionsEditorProps {
  bankId: string;
  topicId: string;
}

/**
 * Редактор вопросов одного банка (#623): список вопросов с типом/сложностью +
 * создание/редактирование (через `QuestionFormDialog`) и удаление. Открывается
 * инлайн под строкой банка в `BanksPanel`. Mobile-first.
 */
export function BankQuestionsEditor({ bankId, topicId }: BankQuestionsEditorProps) {
  const { data: questions, isLoading, error } = useQuery(
    trainerQuestionAdminQueryOptions.bankQuestionsOptions(bankId),
  );
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<TrainerQuestionAdmin | null>(null);

  const openCreate = () => {
    setEditing(null);
    setDialogOpen(true);
  };

  const openEdit = (question: TrainerQuestionAdmin) => {
    setEditing(question);
    setDialogOpen(true);
  };

  let body: React.ReactNode;
  if (isLoading) {
    body = (
      <div className="space-y-2">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
      </div>
    );
  } else if (error) {
    body = (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить вопросы")}
      </p>
    );
  } else if (!questions || questions.length === 0) {
    body = (
      <EmptyState
        variant="dashed"
        icon={Icons.quiz}
        title="Вопросов пока нет"
        description="Добавьте первый вопрос — он появится в тренажёре после публикации темы."
        action={
          <Button size="sm" onClick={openCreate}>
            <Icons.add className="size-4" />
            Добавить вопрос
          </Button>
        }
      />
    );
  } else {
    body = (
      <ul className="space-y-2">
        {questions.map((question) => (
          <QuestionRow
            key={question.id}
            question={question}
            bankId={bankId}
            topicId={topicId}
            onEdit={() => openEdit(question)}
          />
        ))}
      </ul>
    );
  }

  return (
    <div className="space-y-3 rounded-lg border border-border/50 bg-muted/20 p-3">
      <div className="flex items-center justify-between gap-2">
        <h4 className="text-sm font-semibold">Вопросы банка</h4>
        {questions && questions.length > 0 && (
          <Button size="sm" variant="outline" onClick={openCreate} className="min-touch">
            <Icons.add className="size-4" />
            Добавить
          </Button>
        )}
      </div>

      {body}

      {/* key ремоунтит форму под текущий вопрос → initial state из props без useEffect. */}
      <QuestionFormDialog
        key={editing?.id ?? "new"}
        open={dialogOpen}
        onOpenChange={setDialogOpen}
        bankId={bankId}
        topicId={topicId}
        question={editing}
      />
    </div>
  );
}

function QuestionRow({
  question,
  bankId,
  topicId,
  onEdit,
}: {
  question: TrainerQuestionAdmin;
  bankId: string;
  topicId: string;
  onEdit: () => void;
}) {
  const deleteMutation = useDeleteTrainerQuestion();
  const [deleteOpen, setDeleteOpen] = useState(false);

  return (
    <li className="flex items-start gap-3 rounded-lg border border-border/60 bg-card p-3">
      <div className="min-w-0 flex-1 space-y-1.5">
        <p className="text-sm leading-snug">{question.stem}</p>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge variant="outline" className="text-[11px]">
            {TYPE_LABELS[question.type] ?? question.type}
          </Badge>
          {question.difficulty && (
            <span
              className={`rounded px-1.5 py-0.5 text-[11px] font-medium ${TRAINER_DIFFICULTY_VISUALS[question.difficulty]?.badgeClass ?? "bg-muted text-muted-foreground"}`}
            >
              {TRAINER_DIFFICULTY_VISUALS[question.difficulty]?.label ?? question.difficulty}
            </span>
          )}
          {question.section && (
            <span className="rounded bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground">
              {question.section}
            </span>
          )}
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-1">
        <Button
          variant="ghost"
          size="icon"
          className="min-touch text-muted-foreground"
          onClick={onEdit}
          aria-label="Редактировать вопрос"
        >
          <Icons.edit className="size-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="min-touch text-muted-foreground hover:text-destructive"
          onClick={() => setDeleteOpen(true)}
          aria-label="Удалить вопрос"
        >
          <Icons.delete className="size-4" />
        </Button>
      </div>

      <DeleteConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title="Удалить вопрос?"
        description="Вопрос и его варианты будут удалены. Это действие необратимо."
        confirmLabel="Удалить"
        isPending={deleteMutation.isPending}
        onConfirm={() =>
          deleteMutation.mutateAsync({ questionId: question.id, bankId, topicId })
        }
      />
    </li>
  );
}
