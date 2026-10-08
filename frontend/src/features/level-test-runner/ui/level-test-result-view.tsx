"use client";

import {
  isAiGradingPending,
  isFullLevelTestResult,
  levelTestQueryOptions,
} from "@/entities/level-test";
import { ErrorType, getErrorMessage, isEnvelopeError, isForbiddenError } from "@/shared/api";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { getOrCreateAnonymousId } from "@/shared/lib/anonymous-id";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import Link from "next/link";
import { useEffect } from "react";
import { useClaimLevelTestAttempts } from "../model/use-claim-level-test-attempts";
import { LevelTestResultFull } from "./level-test-result-full";
import { LevelTestResultTeaser } from "./level-test-result-teaser";

type ResultErrorKind = "unauthorized" | "forbidden" | "not-found" | "unknown";

/**
 * Классификация ошибки результата. Клеймленная попытка для анонима — голый
 * 401 без envelope (interceptor пробрасывает его как есть для
 * unauthenticated-посетителей), чужому юзеру — 403 (`ForbiddenError`).
 */
function classifyResultError(error: unknown): ResultErrorKind {
  if (isEnvelopeError(error)) {
    if (error.type === ErrorType.AUTHENTICATION) return "unauthorized";
    if (error.type === ErrorType.NOT_FOUND) return "not-found";
    return "unknown";
  }
  if (isForbiddenError(error)) return "forbidden";
  if (typeof error === "object" && error !== null) {
    const response = (error as { response?: { status?: number } }).response;
    if (response?.status === 401) return "unauthorized";
  }
  return "unknown";
}

interface LevelTestResultViewProps {
  attemptId: string;
}

/**
 * Страница результата `/level-test/result/[attemptId]`: тизер для анонима
 * (lead-gate) или полный разбор владельцу. После возврата с логина один раз
 * клеймит анонимные попытки cookie-id и рефетчит результат. Issue #481.
 */
export function LevelTestResultView({ attemptId }: LevelTestResultViewProps) {
  const session = useSession();
  const { status: authStatus } = session;
  const userId = session.status === "authenticated" ? (session.data.user.id ?? null) : null;
  const browserAnonymousId = authStatus === "loading" ? null : getOrCreateAnonymousId();
  const viewerScope =
    typeof userId === "string"
      ? `user:${userId}`
      : browserAnonymousId
        ? `anonymous:${browserAnonymousId}`
        : `session:${authStatus}`;
  const resultQuery = useQuery({
    ...levelTestQueryOptions.attemptResultOptions(attemptId, viewerScope),
    enabled: authStatus !== "loading",
  });
  const claim = useClaimLevelTestAttempts();

  // Claim-on-return: залогинились и видим тизер → привязываем попытки ровно один
  // раз за mount (mutation выходит из isIdle после первого вызова).
  const result = resultQuery.data;
  const { isIdle: claimIsIdle, mutate: claimMutate } = claim;
  useEffect(() => {
    if (authStatus !== "unauthenticated") return;
    if (!result || isFullLevelTestResult(result)) return;
    trackGrowthEvent(
      {
        name: "level_test_result_teaser",
        properties: { result_band: result.level },
      },
      { once: `level-test-result-teaser:${attemptId}` },
    );
  }, [attemptId, authStatus, result]);

  useEffect(() => {
    if (authStatus !== "authenticated") return;
    if (!result || isFullLevelTestResult(result)) return;
    if (!claimIsIdle) return;
    if (!browserAnonymousId) return;
    claimMutate(
      { anonymousId: browserAnonymousId, attemptId, viewerScope },
      {
        onSuccess: (claimResult) => {
          if (claimResult.claimedCount === 0) return;
          trackGrowthEvent({
            name: "level_test_claimed",
            properties: { result_band: result.level },
          });
        },
      },
    );
  }, [attemptId, browserAnonymousId, authStatus, result, claimIsIdle, claimMutate, viewerScope]);

  if (resultQuery.isPending) {
    return (
      <div className="mx-auto w-full max-w-2xl space-y-8 py-10">
        <div className="flex flex-col items-center gap-3">
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-16 w-36" />
          <Skeleton className="h-6 w-24" />
        </div>
        <Skeleton className="h-64 w-full rounded-xl" />
      </div>
    );
  }

  if (resultQuery.isError) {
    const error: unknown = resultQuery.error;
    const errorKind = classifyResultError(error);

    if (errorKind === "unauthorized") {
      const loginHref = `${routes.login}?callbackUrl=${encodeURIComponent(
        routes.levelTestResult(attemptId),
      )}`;
      return (
        <EmptyState
          icon={Icons.locked}
          variant="card"
          className="mx-auto my-10 max-w-xl"
          title="Войдите, чтобы увидеть результат"
          description="Этот результат привязан к аккаунту — авторизуйтесь, чтобы открыть полный разбор."
          action={
            <Button asChild>
              <Link href={loginHref} prefetch={false}>
                Войти
              </Link>
            </Button>
          }
        />
      );
    }

    if (errorKind === "forbidden") {
      return (
        <EmptyState
          icon={Icons.locked}
          variant="card"
          className="mx-auto my-10 max-w-xl"
          title="Результат принадлежит другому аккаунту"
          description="Пройдите тест сами — это займёт около 15 минут."
          action={
            <Button asChild>
              <Link href={routes.levelTest}>Пройти тест</Link>
            </Button>
          }
        />
      );
    }

    if (errorKind === "not-found") {
      return (
        <EmptyState
          icon={Icons.searchEmpty}
          variant="card"
          className="mx-auto my-10 max-w-xl"
          title="Результат не найден"
          description="Возможно, ссылка устарела. Пройдите тест заново."
          action={
            <Button asChild>
              <Link href={routes.levelTest}>Пройти тест</Link>
            </Button>
          }
        />
      );
    }

    return (
      <EmptyState
        icon={Icons.error}
        variant="card"
        className="mx-auto my-10 max-w-xl"
        title="Не удалось загрузить результат"
        description={getErrorMessage(error, "Попробуйте обновить страницу")}
        action={
          <Button variant="outline" onClick={() => resultQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  // Открытые вопросы ещё проверяются → цифры не показываем (тизер без опенов
  // вводил бы в заблуждение). Query поллит каждые ~4с — страница обновится сама.
  if (isAiGradingPending(resultQuery.data.aiGradingStatus)) {
    return (
      <div className="mx-auto my-16 flex w-full max-w-xl flex-col items-center gap-4 rounded-xl border border-border/60 bg-card px-6 py-12 text-center">
        <Icons.loading className="size-8 animate-spin text-primary" aria-hidden />
        <h1 className="text-xl font-semibold">Проверяем твои ответы</h1>
        <p className="text-sm text-muted-foreground">
          Развёрнутые ответы оцениваются по эталонным критериям — обычно это занимает меньше минуты.
          Страница обновится сама, уходить не обязательно.
        </p>
      </div>
    );
  }

  if (isFullLevelTestResult(resultQuery.data)) {
    return <LevelTestResultFull result={resultQuery.data} />;
  }

  return (
    <LevelTestResultTeaser
      teaser={resultQuery.data}
      viewerAuthenticated={session.status === "authenticated"}
      isClaiming={claim.isPending || resultQuery.isRefetching}
    />
  );
}
