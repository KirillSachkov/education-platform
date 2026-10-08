"use client";

import { QuizPickerDialog, quizQueryOptions } from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { AccessTypeBadge } from "@/shared/ui/components/access-type-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { CreateQuizDialog } from "./create-quiz-dialog";

interface MaterialQuizBindingSectionProps {
  /** Текущая привязка (`materials.quiz_id`) — controlled-значение из формы материала. */
  quizId: string | null;
  /**
   * Меняет привязку: id ⇒ привязать, `null` ⇒ отвязать. Форма материала
   * немедленно шлёт PATCH с PUT-семантикой quizId (#489) — без полного сабмита.
   */
  onQuizIdChange: (quizId: string | null) => void;
  className?: string;
}

/**
 * Блок «Квиз» на странице редактирования материала (#494): квиз — standalone
 * сущность из библиотеки `/author/quizzes`, материал лишь ссылается на него.
 * Привязан → карточка квиза с кнопками «Редактировать в библиотеке» / «Отвязать»;
 * нет → «Привязать квиз» (picker по библиотеке) или «Создать новый» (DRAFT +
 * автопривязка). Содержимое квиза здесь не редактируется — только в библиотеке.
 */
export function MaterialQuizBindingSection({
  quizId,
  onQuizIdChange,
  className,
}: MaterialQuizBindingSectionProps) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);

  return (
    <section className={cn("space-y-4", className)}>
      <div className="flex items-center gap-2">
        <Icons.quiz size={16} className="text-primary" />
        <h2 className="text-sm font-semibold">Тест</h2>
      </div>

      {quizId ? (
        <BoundQuizCard quizId={quizId} onUnbind={() => onQuizIdChange(null)} />
      ) : (
        <div className="flex flex-col items-start gap-3 rounded-xl border border-dashed border-border/70 bg-card/30 p-5">
          <p className="text-sm text-muted-foreground">
            Привяжите тест «Проверь себя» из библиотеки — студенты увидят его под материалом
            после публикации теста. Один тест можно переиспользовать в нескольких материалах.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" onClick={() => setPickerOpen(true)}>
              <Icons.link className="size-3.5" />
              Привязать тест
            </Button>
            <Button type="button" variant="ghost" size="sm" onClick={() => setCreateOpen(true)}>
              <Icons.add className="size-3.5" />
              Создать новый
            </Button>
          </div>
        </div>
      )}

      <QuizPickerDialog
        open={pickerOpen}
        onOpenChange={setPickerOpen}
        title="Привязать тест"
        description="Выберите тест из библиотеки — материал будет ссылаться на него"
        onSelect={(selectedId) => {
          onQuizIdChange(selectedId);
          setPickerOpen(false);
        }}
        isPending={false}
        createAction={
          <Button
            type="button"
            variant="outline"
            className="shrink-0"
            onClick={() => {
              setPickerOpen(false);
              setCreateOpen(true);
            }}
          >
            <Icons.add size={14} />
            Создать новый
          </Button>
        }
      />

      <CreateQuizDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(createdId) => onQuizIdChange(createdId)}
      />
    </section>
  );
}

function BoundQuizCard({ quizId, onUnbind }: { quizId: string; onUnbind: () => void }) {
  const { data: quiz, isLoading, error } = useQuery(quizQueryOptions.authorQuizOptions(quizId));

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 rounded-xl border border-border/70 bg-card/50 p-5 text-sm text-muted-foreground">
        <Icons.loading className="size-4 animate-spin" />
        Загрузка теста…
      </div>
    );
  }

  if (error || !quiz) {
    return (
      <div className="flex flex-col items-start gap-3 rounded-xl border border-border/70 bg-card/50 p-5">
        <p className="text-sm text-destructive">
          {getErrorMessage(error, "Не удалось загрузить привязанный тест")}
        </p>
        <Button type="button" variant="outline" size="sm" onClick={onUnbind}>
          <Icons.unlink className="size-3.5" />
          Отвязать
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-border/70 bg-card/50 p-5 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex min-w-0 items-start gap-3">
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-violet-500/15">
          <Icons.quiz size={15} className="text-violet-500" />
        </span>
        <div className="min-w-0 space-y-1">
          <p className="truncate text-sm font-semibold">{quiz.title}</p>
          <div className="flex flex-wrap items-center gap-2">
            <StatusBadge status={quiz.status} />
            <AccessTypeBadge accessType={quiz.accessType} />
            <span className="text-[11px] tabular-nums text-muted-foreground">
              {quiz.questions.length}{" "}
              {pluralize(quiz.questions.length, "вопрос", "вопроса", "вопросов")}
            </span>
          </div>
        </div>
      </div>
      <div className="flex shrink-0 flex-wrap gap-2">
        <Button type="button" variant="outline" size="sm" asChild>
          <Link href={routes.authorQuizEdit(quiz.id)} target="_blank" rel="noopener">
            <Icons.edit className="size-3.5" />
            Редактировать в библиотеке
          </Link>
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="text-destructive hover:text-destructive"
          onClick={onUnbind}
        >
          <Icons.unlink className="size-3.5" />
          Отвязать
        </Button>
      </div>
    </div>
  );
}
