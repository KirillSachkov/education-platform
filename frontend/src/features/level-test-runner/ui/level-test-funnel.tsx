"use client";

import { levelTestQueryOptions, type SubmitLevelTestAnswerItem } from "@/entities/level-test";
import type { LevelTestAnswersState } from "../model/answers";
import { getErrorMessage } from "@/shared/api";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { getOrCreateAnonymousId } from "@/shared/lib/anonymous-id";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { useRouter } from "next/navigation";
import { useState, useSyncExternalStore } from "react";
import { useSubmitLevelTestAttempt } from "../model/use-submit-level-test-attempt";
import { LevelTestLandingHero } from "./level-test-landing-hero";
import { LevelTestRunner } from "./level-test-runner";

const subscribeNoop = () => () => {};
const LEVEL_TEST_LAST_ATTEMPT_STORAGE_KEY = "level-test:last-attempt-id";

function readLastAttemptId(storageKey: string | null): string | null {
  if (!storageKey) return null;
  try {
    return sessionStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

function saveLastAttemptId(storageKey: string | null, attemptId: string): void {
  if (!storageKey) return;
  try {
    sessionStorage.setItem(storageKey, attemptId);
  } catch {
    // sessionStorage может быть недоступен (privacy mode) — fallback не критичен
  }
}

/** Черновик прохождения (#528): переживает reload в текущей вкладке. */
interface LevelTestDraft {
  answers: LevelTestAnswersState;
  index: number;
  updatedAt: string;
}

type LevelTestOwnerScope = `user:${string}` | `anonymous:${string}`;

const draftKey = (quizId: string, ownerScope: LevelTestOwnerScope) =>
  `level-test-draft:${quizId}:${ownerScope}`;
const lastAttemptKey = (ownerScope: LevelTestOwnerScope) =>
  `${LEVEL_TEST_LAST_ATTEMPT_STORAGE_KEY}:${ownerScope}`;

function readDraftRaw(storageKey: string | null): string | null {
  if (!storageKey) return null;
  try {
    return sessionStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

function saveDraft(storageKey: string | null, answers: LevelTestAnswersState, index: number): void {
  if (!storageKey) return;
  try {
    const draft: LevelTestDraft = { answers, index, updatedAt: new Date().toISOString() };
    sessionStorage.setItem(storageKey, JSON.stringify(draft));
  } catch {
    // Приватный режим/квота — прохождение продолжается без персиста.
  }
}

function clearDraft(storageKey: string | null): void {
  if (!storageKey) return;
  try {
    sessionStorage.removeItem(storageKey);
  } catch {
    // ignore
  }
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return (
    typeof value === "object" &&
    value !== null &&
    !Array.isArray(value) &&
    Object.getPrototypeOf(value) === Object.prototype
  );
}

function parseDraft(raw: string | null, questionCount: number): LevelTestDraft | null {
  if (!raw) return null;
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!isPlainObject(parsed) || !isPlainObject(parsed.answers)) return null;
    if (
      typeof parsed.index !== "number" ||
      !Number.isFinite(parsed.index) ||
      !Number.isInteger(parsed.index) ||
      parsed.index < 0 ||
      parsed.index >= questionCount
    ) {
      return null;
    }
    if (typeof parsed.updatedAt !== "string" || !Number.isFinite(Date.parse(parsed.updatedAt))) {
      return null;
    }
    const answersValid = Object.values(parsed.answers).every(
      (answer) =>
        isPlainObject(answer) &&
        Array.isArray(answer.selectedOptionIds) &&
        answer.selectedOptionIds.every((optionId) => typeof optionId === "string") &&
        typeof answer.textAnswer === "string",
    );
    if (!answersValid) return null;

    return {
      answers: parsed.answers as LevelTestAnswersState,
      index: parsed.index,
      updatedAt: parsed.updatedAt,
    };
  } catch {
    return null;
  }
}

/**
 * Воронка `/level-test`: state machine intro → questions → submitting.
 * Аноним сабмитит с cookie `plu_anon_id` (lead-gate), auth-юзер — от своего
 * имени; после сабмита — переход на страницу результата. Issue #481.
 */
export function LevelTestFunnel() {
  const testQuery = useQuery(levelTestQueryOptions.activeLevelTestOptions());
  const [stage, setStage] = useState<"intro" | "questions">("intro");
  const [draftRestored, setDraftRestored] = useState(false);
  const session = useSession();
  const router = useRouter();
  const isAuthenticated = session.status === "authenticated";
  const draftAnonymousId = session.status === "unauthenticated" ? getOrCreateAnonymousId() : null;
  const authenticatedUserId = isAuthenticated ? session.data.user.id : null;
  const draftOwnerScope: LevelTestOwnerScope | null = authenticatedUserId
    ? `user:${authenticatedUserId}`
    : draftAnonymousId
      ? `anonymous:${draftAnonymousId}`
      : null;
  const lastAttemptStorageKey = draftOwnerScope ? lastAttemptKey(draftOwnerScope) : null;
  const viewerScope = draftOwnerScope ?? `session:${session.status}`;
  const submit = useSubmitLevelTestAttempt();

  // Уже проходил тест → на лендинге показываем последний результат (#528).
  // Только для auth: эндпоинт требует логина, аноним получил бы 401 → logout.
  const latestQuery = useQuery({
    ...levelTestQueryOptions.myLatestAttemptOptions(viewerScope),
    enabled: isAuthenticated,
  });

  // Hydration-safe чтение sessionStorage: сервер всегда видит null.
  const lastAttemptId = useSyncExternalStore(
    subscribeNoop,
    () => readLastAttemptId(lastAttemptStorageKey),
    () => null,
  );

  // Hydration-safe чтение черновика прохождения (#528).
  const quizIdForDraft = testQuery.data?.id ?? null;
  const draftStorageKey =
    quizIdForDraft && draftOwnerScope ? draftKey(quizIdForDraft, draftOwnerScope) : null;
  const draftRaw = useSyncExternalStore(
    subscribeNoop,
    () => readDraftRaw(draftStorageKey),
    () => null,
  );
  const draft = parseDraft(draftRaw, testQuery.data?.questions.length ?? 0);

  // Незавершённое прохождение восстанавливаем сразу (adjust-state-during-render):
  // после reload юзер возвращается к своему вопросу, а не на лендинг.
  if (!draftRestored && draft && stage === "intro") {
    setDraftRestored(true);
    setStage("questions");
  }

  if (testQuery.isPending) {
    return (
      <section className="mx-auto flex w-full max-w-3xl flex-col items-center gap-6 py-10 sm:py-16">
        <Skeleton className="h-6 w-44" />
        <Skeleton className="h-10 w-full max-w-xl" />
        <Skeleton className="h-12 w-full max-w-lg" />
        <Skeleton className="h-11 w-40" />
      </section>
    );
  }

  if (testQuery.isError) {
    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        className="mx-auto my-10 max-w-xl"
        title="Не удалось загрузить тест"
        description={getErrorMessage(testQuery.error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => testQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  if (testQuery.data === null) {
    return (
      <EmptyState
        icon={Icons.pending}
        variant="card"
        className="mx-auto my-10 max-w-xl"
        title="Тест готовится"
        description="Скоро здесь можно будет проверить свой уровень .NET-разработчика — загляните позже."
      />
    );
  }

  const test = testQuery.data;

  const handleSubmit = (answers: SubmitLevelTestAnswerItem[]) => {
    if (submit.isPending) return;
    const anonymousId = isAuthenticated ? null : (draftAnonymousId ?? getOrCreateAnonymousId());
    submit.mutate(
      { quizId: test.id, anonymousId, answers, viewerScope },
      {
        onSuccess: (result) => {
          saveLastAttemptId(lastAttemptStorageKey, result.attemptId);
          clearDraft(draftStorageKey);
          trackGrowthEvent({
            name: "level_test_completed",
            properties: { test_id: test.id },
          });
          router.push(routes.levelTestResult(result.attemptId));
        },
      },
    );
  };

  const handleStart = () => {
    trackGrowthEvent({
      name: "level_test_started",
      properties: { test_id: test.id },
    });
    setStage("questions");
  };

  if (stage === "intro") {
    return (
      <LevelTestLandingHero
        test={test}
        lastAttemptId={lastAttemptId}
        latestResult={latestQuery.data ?? null}
        hasDraft={draft !== null}
        onStart={handleStart}
      />
    );
  }

  return (
    <div className="py-6 sm:py-10">
      <LevelTestRunner
        key={draftStorageKey ?? "no-draft-owner"}
        test={test}
        initialAnswers={draft?.answers}
        initialIndex={draft?.index}
        onProgress={(answers, index) => {
          saveDraft(draftStorageKey, answers, index);
        }}
        onExit={() => setStage("intro")}
        onSubmit={handleSubmit}
        isSubmitting={submit.isPending}
      />
    </div>
  );
}
