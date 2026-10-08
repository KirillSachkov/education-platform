"use client";

import type {
  MockInterviewBuilderDto,
  MockInterviewBuilderQuestion,
  QuestionBankItem,
} from "@/entities/mock-interview";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";
import { toast } from "sonner";
import { usePublishMockInterview } from "../model/use-publish-mock-interview";
import { useUpdateMockInterview } from "../model/use-update-mock-interview";
import { QuestionBankPicker, questionRefKey } from "./question-bank-picker";

interface MockInterviewEditorProps {
  interview: MockInterviewBuilderDto;
}

/**
 * Авторский редактор мок-собеса (#585): название + описание, «Показывать N из M
 * вопросов» (случайная подвыборка на сессию; пусто = весь набор), курированный
 * набор (удаляемый, со стемом + тема) и пикер банка. «Сохранить» — PUT replace
 * целиком; «Опубликовать» — DRAFT → PUBLISHED. Mobile-first.
 */
export function MockInterviewEditor({ interview }: MockInterviewEditorProps) {
  const [title, setTitle] = useState(interview.title);
  const [description, setDescription] = useState(interview.description ?? "");
  const [perSession, setPerSession] = useState(
    interview.questionsPerSession == null ? "" : String(interview.questionsPerSession),
  );
  const [questions, setQuestions] = useState<MockInterviewBuilderQuestion[]>(interview.questions);

  const updateMutation = useUpdateMockInterview();
  const publishMutation = usePublishMockInterview();
  const isBusy = updateMutation.isPending || publishMutation.isPending;

  const status = interview.isPublished ? "PUBLISHED" : "DRAFT";
  const curatedCount = questions.length;
  const selectedKeys = new Set(questions.map((q) => questionRefKey(q.questionId)));

  const handleAdd = (item: QuestionBankItem) => {
    const key = questionRefKey(item.questionId);
    if (selectedKeys.has(key)) return;
    setQuestions((prev) => [
      ...prev,
      {
        questionId: item.questionId,
        text: item.text,
        type: item.type,
        difficulty: item.difficulty,
        topicId: item.topicId,
        topicTitle: item.topicTitle,
      },
    ]);
  };

  const handleRemove = (questionId: string) => {
    setQuestions((prev) => prev.filter((q) => q.questionId !== questionId));
  };

  const buildBody = () => {
    const trimmedTitle = title.trim();
    if (trimmedTitle.length === 0) {
      toast.error("Введите название мок-собеса");
      return null;
    }

    const trimmedPerSession = perSession.trim();
    let questionsPerSession: number | null = null;
    if (trimmedPerSession !== "") {
      const parsed = Number(trimmedPerSession);
      if (!Number.isInteger(parsed) || parsed < 1 || parsed > 200) {
        toast.error("«Показывать N из M» — целое число от 1 до 200 (или пусто)");
        return null;
      }
      questionsPerSession = parsed;
    }

    return {
      title: trimmedTitle,
      description: description.trim() === "" ? null : description.trim(),
      questionsPerSession,
      questions: questions.map((q) => ({ questionId: q.questionId })),
    };
  };

  const handleSave = () => {
    const body = buildBody();
    if (!body) return;
    updateMutation.mutate({ interviewId: interview.id, body });
  };

  const handlePublish = () => {
    if (curatedCount === 0) {
      toast.error("Добавьте хотя бы один вопрос, чтобы опубликовать");
      return;
    }
    publishMutation.mutate(interview.id);
  };

  return (
    <div className="space-y-8">
      {/* ===== Header: название + статус + действия ===== */}
      <div className="space-y-3">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="min-w-0 flex-1 space-y-1.5">
            <div className="flex items-center gap-2">
              <Label htmlFor="mock-title">Название мок-собеса</Label>
              <StatusBadge status={status} />
            </div>
            <Input
              id="mock-title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              maxLength={200}
              disabled={isBusy}
            />
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Button type="button" onClick={handleSave} disabled={isBusy}>
              {updateMutation.isPending ? (
                <Icons.loading className="size-4 animate-spin" />
              ) : (
                <Icons.save className="size-4" />
              )}
              Сохранить
            </Button>
            {!interview.isPublished && (
              <Button
                type="button"
                variant="outline"
                onClick={handlePublish}
                disabled={isBusy || curatedCount === 0}
                title={
                  curatedCount === 0
                    ? "Добавьте хотя бы один вопрос, чтобы опубликовать"
                    : undefined
                }
              >
                {publishMutation.isPending ? (
                  <Icons.loading className="size-4 animate-spin" />
                ) : (
                  <Icons.send className="size-4" />
                )}
                Опубликовать
              </Button>
            )}
          </div>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="mock-description">Описание (необязательно)</Label>
          <Textarea
            id="mock-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            maxLength={1000}
            rows={2}
            placeholder="Кому подходит этот мок-собес — например: джуниор-бэкенд на .NET"
            disabled={isBusy}
          />
        </div>

        {interview.isPublished && (
          <p className="flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-sm text-amber-600 dark:text-amber-400">
            <Icons.warning className="mt-0.5 size-4 shrink-0" />
            Мок-собес опубликован — изменения станут видны студентам сразу после сохранения.
          </p>
        )}
      </div>

      {/* ===== Размер подвыборки ===== */}
      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Сколько вопросов показывать</h2>
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1.5">
            <Label htmlFor="mock-per-session">Показывать N из {curatedCount}</Label>
            <Input
              id="mock-per-session"
              type="number"
              min={1}
              max={200}
              inputMode="numeric"
              value={perSession}
              onChange={(event) => setPerSession(event.target.value)}
              placeholder="Все"
              disabled={isBusy}
              className="max-w-32"
            />
          </div>
          <p className="pb-2 text-xs text-muted-foreground">
            Случайная подвыборка вопросов на каждую сессию. Оставь пустым, чтобы показывать все{" "}
            {curatedCount} {pluralize(curatedCount, "вопрос", "вопроса", "вопросов")}.
          </p>
        </div>
      </section>

      {/* ===== Курированный набор ===== */}
      <section className="space-y-3">
        <div className="flex flex-wrap items-baseline gap-2">
          <h2 className="text-lg font-semibold">Вопросы набора</h2>
          <span className="text-sm text-muted-foreground">
            {curatedCount} {pluralize(curatedCount, "вопрос", "вопроса", "вопросов")}
          </span>
        </div>

        {curatedCount === 0 ? (
          <p className="rounded-lg border border-dashed border-border/70 bg-card/30 p-4 text-sm text-muted-foreground">
            Набор пуст — добавьте вопросы из банка ниже. Сохранение не забудьте: набор заменяется
            целиком.
          </p>
        ) : (
          <ul className="space-y-2">
            {questions.map((question) => (
              <li
                key={questionRefKey(question.questionId)}
                className="flex items-start gap-3 rounded-xl border border-border/60 bg-card/50 p-3"
              >
                <div className="min-w-0 flex-1 space-y-1">
                  <p className="text-sm leading-snug">{question.text}</p>
                  <div className="flex flex-wrap items-center gap-1.5 text-[11px] text-muted-foreground">
                    {question.topicTitle && (
                      <span className="rounded bg-muted px-1.5 py-0.5">{question.topicTitle}</span>
                    )}
                    {question.difficulty && (
                      <span className="rounded bg-muted px-1.5 py-0.5">{question.difficulty}</span>
                    )}
                    <span className="rounded bg-muted px-1.5 py-0.5">{question.type}</span>
                  </div>
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="size-8 shrink-0 text-muted-foreground hover:text-destructive"
                  onClick={() => handleRemove(question.questionId)}
                  disabled={isBusy}
                  aria-label="Убрать вопрос из набора"
                >
                  <Icons.delete className="size-4" />
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* ===== Пикер банка ===== */}
      <section className={cn("space-y-3", isBusy && "pointer-events-none opacity-60")}>
        <div className="space-y-1">
          <h2 className="text-lg font-semibold">Добавить из банка вопросов</h2>
          <p className="text-sm text-muted-foreground">
            Вопросы из банков опубликованных тем. Фильтруй по треку, теме и тексту — добавляй в
            набор.
          </p>
        </div>
        <QuestionBankPicker selectedKeys={selectedKeys} onAdd={handleAdd} disabled={isBusy} />
      </section>
    </div>
  );
}
