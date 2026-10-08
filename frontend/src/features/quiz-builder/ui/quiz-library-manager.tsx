"use client";

import {
  quizQueryOptions,
  type MyQuizSummaryDto,
  type QuizAccessType,
  type QuizStatus,
} from "@/entities/quiz";
import { getErrorMessage } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { formatRelativeDate } from "@/shared/lib/date";
import { pluralize } from "@/shared/lib/pluralize";
import { AccessTypeBadge } from "@/shared/ui/components/access-type-badge";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Input } from "@/shared/ui/kit/input";
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
import { CreateQuizDialog } from "./create-quiz-dialog";
import { QuizBuilderForm } from "./quiz-builder-form";

type StatusFilter = QuizStatus | "all";
type AccessFilter = QuizAccessType | "all";

const STATUS_FILTER_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: "all", label: "Все статусы" },
  { value: "DRAFT", label: "Черновики" },
  { value: "PUBLISHED", label: "Опубликованные" },
  { value: "ARCHIVED", label: "В архиве" },
];

const ACCESS_FILTER_OPTIONS: { value: AccessFilter; label: string }[] = [
  { value: "all", label: "Любой доступ" },
  { value: "PUBLIC", label: "Публичные" },
  { value: "REGISTERED", label: "Авторизованным" },
  { value: "ENROLLED", label: "Записанным" },
];

interface QuizLibraryManagerProps {
  /** `?quiz=` из URL — авто-раскрыть редактор этого квиза (deep-link из привязок). */
  initialQuizId?: string;
}

/**
 * Страница «Квизы» автора (#494): библиотека standalone-квизов (без LEVEL_TEST —
 * у него своя страница «Тест уровня»). Карточка: title + статус + доступ +
 * usage-метрики; клик раскрывает инлайн-редактор (зеркало /author/level-test).
 * «Создать квиз» — диалог с названием → DRAFT → редактор.
 */
export function QuizLibraryManager({ initialQuizId }: QuizLibraryManagerProps) {
  const { data: quizzes, isLoading, error } = useQuery(quizQueryOptions.myQuizzesOptions());
  const [createOpen, setCreateOpen] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(initialQuizId ?? null);
  const [search, setSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [accessFilter, setAccessFilter] = useState<AccessFilter>("all");

  const libraryQuizzes = (quizzes ?? []).filter((quiz) => quiz.purpose !== "LEVEL_TEST");

  const query = search.trim().toLocaleLowerCase("ru");
  const filtered = libraryQuizzes.filter((quiz) => {
    if (statusFilter !== "all" && quiz.status !== statusFilter) return false;
    if (accessFilter !== "all" && quiz.accessType !== accessFilter) return false;
    if (query.length > 0 && !quiz.title.toLocaleLowerCase("ru").includes(query)) return false;
    return true;
  });

  const hasActiveFilters =
    query.length > 0 || statusFilter !== "all" || accessFilter !== "all";

  const resetFilters = () => {
    setSearch("");
    setStatusFilter("all");
    setAccessFilter("all");
  };

  let body: React.ReactNode;
  if (isLoading) {
    body = (
      <div className="space-y-3">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-20 w-full" />
      </div>
    );
  } else if (error) {
    body = (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить тесты")}
      </p>
    );
  } else if (libraryQuizzes.length === 0) {
    body = (
      <EmptyState
        variant="dashed"
        icon={Icons.quiz}
        title="Тестов пока нет"
        description="Создайте тест «Проверь себя» — его можно привязать к материалу, добавить в модуль курса или в подборку."
        action={
          <Button onClick={() => setCreateOpen(true)}>
            <Icons.add className="size-4" />
            Создать тест
          </Button>
        }
      />
    );
  } else if (filtered.length === 0) {
    body = (
      <EmptyState
        variant="dashed"
        icon={Icons.search}
        title="Ничего не найдено"
        description="По выбранным фильтрам тестов нет. Измените запрос или сбросьте фильтры."
        action={
          <Button variant="outline" onClick={resetFilters}>
            Сбросить фильтры
          </Button>
        }
      />
    );
  } else {
    body = (
      <div className="space-y-3">
        {filtered.map((quiz) => (
          <QuizLibraryCard
            key={quiz.id}
            quiz={quiz}
            isExpanded={expandedId === quiz.id}
            onToggle={() => setExpandedId((current) => (current === quiz.id ? null : quiz.id))}
            onDeleted={() => setExpandedId(null)}
          />
        ))}
      </div>
    );
  }

  return (
    <div className="mx-auto mt-8 max-w-5xl space-y-6 px-4 pb-16">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Тесты</h1>
          <p className="text-sm text-muted-foreground">
            Библиотека тестов «Проверь себя»: один тест можно переиспользовать в материалах,
            модулях курсов и подборках
          </p>
        </div>
        {libraryQuizzes.length > 0 && (
          <Button onClick={() => setCreateOpen(true)}>
            <Icons.add className="size-4" />
            Создать тест
          </Button>
        )}
      </header>

      {!isLoading && !error && libraryQuizzes.length > 0 && (
        <div className="space-y-2">
          <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_auto_auto]">
            <div className="relative min-w-0">
              <Icons.search
                size={14}
                className="pointer-events-none absolute top-1/2 left-3 -translate-y-1/2 text-muted-foreground"
              />
              <Input
                type="search"
                inputMode="search"
                enterKeyHint="search"
                autoComplete="off"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Поиск по названию…"
                className="pl-9"
                aria-label="Поиск тестов по названию"
              />
            </div>
            <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as StatusFilter)}>
              <SelectTrigger className="w-full sm:w-[170px]" aria-label="Фильтр по статусу">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {STATUS_FILTER_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Select value={accessFilter} onValueChange={(v) => setAccessFilter(v as AccessFilter)}>
              <SelectTrigger className="w-full sm:w-[180px]" aria-label="Фильтр по доступу">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ACCESS_FILTER_OPTIONS.map((option) => (
                  <SelectItem key={option.value} value={option.value}>
                    {option.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          {hasActiveFilters && (
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              <span className="tabular-nums">
                Показано {filtered.length} из {libraryQuizzes.length}
              </span>
              <button
                type="button"
                onClick={resetFilters}
                className="font-medium text-primary hover:underline"
              >
                Сбросить
              </button>
            </div>
          )}
        </div>
      )}

      {body}

      <CreateQuizDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(quizId) => setExpandedId(quizId)}
      />
    </div>
  );
}

function QuizLibraryCard({
  quiz,
  isExpanded,
  onToggle,
  onDeleted,
}: {
  quiz: MyQuizSummaryDto;
  isExpanded: boolean;
  onToggle: () => void;
  onDeleted: () => void;
}) {
  return (
    <Card className="gap-0 overflow-hidden p-0">
      <button
        type="button"
        onClick={onToggle}
        className="group flex w-full items-center gap-3 px-4 py-3.5 text-left transition-colors hover:bg-accent/30 sm:px-5"
        aria-expanded={isExpanded}
      >
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-violet-500/15">
          <Icons.quiz size={15} className="text-violet-500" />
        </span>
        <span className="min-w-0 flex-1 space-y-1">
          <span className="block truncate text-sm font-semibold">{quiz.title}</span>
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={quiz.status} />
            <AccessTypeBadge accessType={quiz.accessType} />
            <span className="text-[11px] tabular-nums text-muted-foreground">
              {quiz.questionsCount}{" "}
              {pluralize(quiz.questionsCount, "вопрос", "вопроса", "вопросов")}
            </span>
            <span className="text-[11px] tabular-nums text-muted-foreground">
              в материалах: {quiz.usedByMaterialsCount}
            </span>
            <span className="text-[11px] tabular-nums text-muted-foreground">
              в курсах: {quiz.courseCount}
            </span>
          </span>
        </span>
        <span className="hidden text-[11px] text-muted-foreground sm:inline">
          {formatRelativeDate(quiz.updatedAt)}
        </span>
        <Icons.chevronDown
          size={14}
          className={cn(
            "shrink-0 text-muted-foreground/50 transition-transform",
            isExpanded && "rotate-180",
          )}
        />
      </button>

      {isExpanded && (
        <div className="border-t border-border/60 px-4 py-4 sm:px-5">
          <QuizEditorPanel
            quizId={quiz.id}
            hasCourseBinding={quiz.courseCount > 0}
            onDeleted={onDeleted}
          />
        </div>
      )}
    </Card>
  );
}

function QuizEditorPanel({
  quizId,
  hasCourseBinding,
  onDeleted,
}: {
  quizId: string;
  hasCourseBinding: boolean;
  onDeleted: () => void;
}) {
  const { data: quiz, isLoading, error } = useQuery(quizQueryOptions.authorQuizOptions(quizId));

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-10 w-full max-w-xl" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (error || !quiz) {
    return (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить тест")}
      </p>
    );
  }

  return (
    <QuizBuilderForm
      key={`${quiz.id}:${quiz.updatedAt}`}
      quiz={quiz}
      hasCourseBinding={hasCourseBinding}
      onDeleted={onDeleted}
    />
  );
}
