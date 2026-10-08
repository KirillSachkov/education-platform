"use client";

import { pluralize } from "@/shared/lib/pluralize";
import { AccessTypeBadge } from "@/shared/ui/components/access-type-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { quizQueryOptions } from "../api";
import type { MyQuizSummaryDto, QuizPurpose } from "../types";

interface QuizPickerDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  onSelect: (quizId: string) => void;
  isPending: boolean;
  /** Квизы, уже привязанные в целевом контексте — скрываются из списка. */
  excludedIds?: Set<string>;
  /** Slot для кнопки «Создать новый» рядом с поиском (рендерит caller). */
  createAction?: React.ReactNode;
}

/** LEVEL_TEST не размещается в материалах/модулях/подборках — у него своя страница. */
const PICKABLE_PURPOSES: ReadonlySet<QuizPurpose> = new Set(["MATERIAL_CHECK"]);

/**
 * Picker квиза из авторской библиотеки `/quizzes/mine/` (зеркало
 * MaterialPickerDialog). Список без пагинации — мини-библиотека одного автора,
 * поиск фильтрует на клиенте. DRAFT-квизы выбирать можно (как DRAFT-материалы):
 * у студентов они скрыты PUBLISHED-фильтром, автор видит статус на бейдже.
 */
export function QuizPickerDialog({
  open,
  onOpenChange,
  title,
  description,
  onSelect,
  isPending,
  excludedIds,
  createAction,
}: QuizPickerDialogProps) {
  const [search, setSearch] = useState("");
  const { data: quizzes, isLoading } = useQuery({
    ...quizQueryOptions.myQuizzesOptions(),
    enabled: open,
  });

  const normalizedSearch = search.trim().toLowerCase();
  const items = (quizzes ?? [])
    .filter((quiz) => PICKABLE_PURPOSES.has(quiz.purpose))
    .filter((quiz) => !excludedIds?.has(quiz.id))
    .filter(
      (quiz) =>
        normalizedSearch.length === 0 || quiz.title.toLowerCase().includes(normalizedSearch),
    );

  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        onOpenChange(nextOpen);
        if (!nextOpen) {
          setSearch("");
        }
      }}
    >
      <DialogContent className="flex max-h-[90vh] max-w-2xl flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="relative min-w-0 flex-1">
            <Icons.search
              size={14}
              className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Поиск теста..."
              className="pl-9"
            />
          </div>
          {createAction}
        </div>

        <div className="max-h-[60vh] overflow-y-auto pr-1">
          <div className="space-y-2">
            {isLoading ? (
              <div className="flex items-center justify-center py-10">
                <Icons.loading className="size-5 animate-spin text-muted-foreground" />
              </div>
            ) : items.length === 0 ? (
              <p className="py-10 text-center text-sm text-muted-foreground">
                {normalizedSearch.length > 0
                  ? "Ничего не найдено по поиску"
                  : "Подходящих тестов нет — создайте новый в библиотеке «Тесты»"}
              </p>
            ) : (
              items.map((quiz) => (
                <QuizListItem key={quiz.id} quiz={quiz} isPending={isPending} onSelect={onSelect} />
              ))
            )}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}

function QuizListItem({
  quiz,
  isPending,
  onSelect,
}: {
  quiz: MyQuizSummaryDto;
  isPending: boolean;
  onSelect: (id: string) => void;
}) {
  return (
    <button
      type="button"
      disabled={isPending}
      onClick={() => onSelect(quiz.id)}
      className="w-full rounded-xl border border-border/70 bg-card/70 p-4 text-left transition-colors hover:border-primary/40 hover:bg-accent/20 disabled:cursor-not-allowed disabled:opacity-60"
    >
      <div className="min-w-0 space-y-2">
        <div className="flex min-w-0 items-start gap-2">
          <Icons.quiz size={15} className="mt-0.5 shrink-0 text-violet-500" />
          <span className="line-clamp-2 break-words text-sm font-semibold [overflow-wrap:anywhere]">
            {quiz.title}
          </span>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <StatusBadge status={quiz.status} />
          <AccessTypeBadge accessType={quiz.accessType} />
          <span className="text-[11px] tabular-nums text-muted-foreground">
            {quiz.questionsCount}{" "}
            {pluralize(quiz.questionsCount, "вопрос", "вопроса", "вопросов")}
          </span>
          {quiz.courseCount > 0 && (
            <span className="text-[11px] tabular-nums text-muted-foreground">
              в курсах: {quiz.courseCount}
            </span>
          )}
        </div>
      </div>
    </button>
  );
}
