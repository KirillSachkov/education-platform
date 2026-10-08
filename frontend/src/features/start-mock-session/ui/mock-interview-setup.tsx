"use client";

import type { MockInterviewSummary } from "@/entities/mock-interview";
import { routes } from "@/shared/config/routes";
import { TRAINER_CARD_SURFACE } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { resolveMockStartAction } from "../lib/resolve-mock-start-action";
import { useStartMockInterview } from "../model/use-start-mock-interview";

/**
 * Подпись объёма собеса для студента: реально доступное число вопросов на сессию
 * (бэкенд считает резолвимые/доступные — висячие ссылки не в счёт, #623). `0` ⇒ для
 * этой симуляции ещё нет вопросов — старт заблокирован, показываем понятную подпись.
 */
function describeScope(interview: MockInterviewSummary): string {
  if (interview.questionCount > 0) {
    const noun = pluralize(interview.questionCount, "вопрос", "вопроса", "вопросов");
    return `${interview.questionCount} ${noun}`;
  }
  return "вопросы готовятся";
}

interface MockInterviewSetupProps {
  interviews: MockInterviewSummary[];
  /** Залогинен ли вызывающий. Аноним просматривает список read-only (#614 F); «Начать» → логин. */
  isAuthenticated: boolean;
  /** Есть ли у пользователя Trainer Pro. `undefined` — статус ещё не загружен (#658). */
  hasPro?: boolean;
  /** Не-подписчик нажал «Начать» — открыть пейволл вместо мутации, дающей 403 (#658). */
  onProRequired: () => void;
}

/**
 * Конфигуратор симуляции собеседования (#568): выбор мок-собеседования
 * (POSITION-подборка) → крупная кнопка «Начать симуляцию» → редирект на сессию.
 * Запускается ПОЛНЫЙ авторский набор вопросов (#585). Full-width hero (не «квадратик»):
 * крупная иконка/заголовок + центральная кнопка. Несколько собесов — выбираемые карточки.
 */
export function MockInterviewSetup({
  interviews,
  isAuthenticated,
  hasPro,
  onProRequired,
}: MockInterviewSetupProps) {
  const router = useRouter();
  const startMockInterview = useStartMockInterview();
  const [selectedId, setSelectedId] = useState<string>(interviews[0].id);

  const selected = interviews.find((item) => item.id === selectedId) ?? interviews[0];
  const isSingle = interviews.length === 1;
  // Бэкенд отдаёт РЕАЛЬНО доступное число вопросов (резолвимые/в банках тем). 0 ⇒ для собеса
  // ещё нет вопросов — старт блокируем заранее, без тоста-ошибки «нет доступных вопросов» (#568).
  const isAvailable = selected.questionCount > 0;

  const handleStart = () => {
    switch (
      resolveMockStartAction({
        isAvailable,
        isAuthenticated,
        hasPro,
        isPending: startMockInterview.isPending,
      })
    ) {
      case "unavailable":
      case "busy":
        return;
      case "login":
        // Аноним: старт требует входа (#614 F) — подсказка + редирект, мутацию не зовём.
        toast.info("Войдите, чтобы начать");
        router.push(`${routes.login}?callbackUrl=${encodeURIComponent(routes.trainer)}`);
        return;
      case "paywall":
        // Не-подписчик (#658): красивый пейволл вместо мутации, дающей 403.
        onProRequired();
        return;
      case "start":
        startMockInterview.mutate(
          { interviewId: selected.id },
          { onSuccess: (created) => router.push(routes.trainerSession(created.id)) },
        );
        return;
    }
  };

  return (
    <div className="space-y-4">
      {!isSingle && (
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {interviews.map((interview) => (
            <InterviewChoiceCard
              key={interview.id}
              interview={interview}
              active={interview.id === selectedId}
              onSelect={() => setSelectedId(interview.id)}
            />
          ))}
        </div>
      )}

      <section
        className={cn(TRAINER_CARD_SURFACE, "t-enter px-5 py-8 sm:px-8 sm:py-12")}
        key={selected.id}
      >
        <div className="mx-auto flex max-w-2xl flex-col items-center text-center">
          <span className="flex size-14 items-center justify-center rounded-2xl bg-primary/10">
            <Icons.briefcase className="size-7 text-primary" aria-hidden="true" />
          </span>
          <h2 className="mt-4 text-2xl font-bold tracking-tight text-balance sm:text-3xl">
            {selected.title}
          </h2>
          {selected.description && (
            <p className="mt-2.5 max-w-xl text-pretty leading-relaxed text-muted-foreground">
              {selected.description}
            </p>
          )}
          <p className="mt-3 text-sm text-muted-foreground">
            {isAvailable
              ? `${describeScope(selected)} · разбор в конце`
              : "Вопросы для этой симуляции скоро появятся"}
          </p>

          <Button
            onClick={handleStart}
            disabled={startMockInterview.isPending || !isAvailable}
            size="lg"
            className="mt-7 h-12 w-full px-8 text-base font-semibold sm:w-auto sm:min-w-[280px]"
          >
            {startMockInterview.isPending && <Icons.loading className="size-4 animate-spin" />}
            {isAvailable ? "Начать симуляцию" : "Вопросы готовятся"}
          </Button>

          {isAvailable && (
            <p className="mt-4 flex items-center justify-center gap-2 text-xs text-muted-foreground">
              <Icons.mic className="size-4 shrink-0 text-primary" aria-hidden="true" />
              Отвечай голосом или текстом — ИИ распознает речь, разберёт ответы и даст фидбэк в конце.
            </p>
          )}
        </div>
      </section>
    </div>
  );
}

/** Выбираемая карточка подборки (когда собесов несколько). */
function InterviewChoiceCard({
  interview,
  active,
  onSelect,
}: {
  interview: MockInterviewSummary;
  active: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      aria-pressed={active}
      className={cn(
        "flex min-h-[44px] flex-col gap-1 rounded-xl border p-4 text-left transition-colors",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:outline-none",
        active ? "border-primary bg-primary/10" : "border-border/60 bg-card/50 hover:bg-accent/40",
      )}
    >
      <span className={cn("font-medium tracking-tight", active && "text-primary")}>
        {interview.title}
      </span>
      <span className="mt-0.5 text-xs text-muted-foreground">{describeScope(interview)}</span>
    </button>
  );
}
