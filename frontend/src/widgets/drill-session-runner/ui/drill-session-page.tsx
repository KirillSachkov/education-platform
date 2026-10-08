"use client";

import { trainerSessionQueryOptions } from "@/entities/trainer-session";
import { getErrorMessage, ForbiddenError } from "@/shared/api";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { DrillSessionRunner } from "./drill-session-runner";

interface DrillSessionPageProps {
  sessionId: string;
}

/**
 * Загрузчик страницы `/trainer/session/{id}` (#568): тянет сессию (scoped по
 * UserId — чужая → 404), затем рендерит раннер (IN_PROGRESS → loop, COMPLETED →
 * review). Изолирует data-fetching от server-page.
 */
export function DrillSessionPage({ sessionId }: DrillSessionPageProps) {
  const sessionQuery = useQuery(trainerSessionQueryOptions.sessionOptions(sessionId));
  const searchParams = useSearchParams();
  // «Тест» из списка вопросов / SRS / закладок (#656) приходит с ctx=review + back=
  // (URL вкладки, откуда запущено): ярлык — не «Тренировка», «К теме» — на ту вкладку.
  // `back` — user-controlled, поэтому пускаем только same-origin относительный путь
  // («/...», но не «//evil.com» protocol-relative) — иначе open-redirect через <Link>.
  const rawBack = searchParams.get("back");
  const backHref =
    rawBack && rawBack.startsWith("/") && !rawBack.startsWith("//") ? rawBack : undefined;
  const modeLabelOverride = searchParams.get("ctx") === "review" ? "Тест" : undefined;

  if (sessionQuery.isPending) {
    return (
      <div className="mx-auto w-full max-w-3xl space-y-4">
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-2 w-full" />
        <Skeleton className="h-48 w-full rounded-xl" />
        <div className="flex justify-between">
          <Skeleton className="h-10 w-24" />
          <Skeleton className="h-10 w-24" />
        </div>
      </div>
    );
  }

  if (sessionQuery.isError) {
    const isForbidden = sessionQuery.error instanceof ForbiddenError;
    return (
      <EmptyState
        icon={isForbidden ? Icons.locked : Icons.searchEmpty}
        variant="card"
        className="mx-auto max-w-xl"
        title={isForbidden ? "Нет доступа к тренировке" : "Тренировка не найдена"}
        description={getErrorMessage(
          sessionQuery.error,
          "Возможно, ссылка устарела или сессия принадлежит другому аккаунту.",
        )}
        action={
          <Button asChild variant="outline">
            <Link href={routes.trainer}>
              <Icons.chevronLeft className="size-4" />К темам
            </Link>
          </Button>
        }
      />
    );
  }

  return (
    <DrillSessionRunner
      session={sessionQuery.data}
      backHref={backHref}
      modeLabelOverride={modeLabelOverride}
    />
  );
}
