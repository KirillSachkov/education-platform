"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";

import {
  TrainerStrengthsPanels,
  trainerStrengthsQueryOptions,
  type TrainerStrengthSampleSize,
  type TrainerStrengthTopic,
} from "@/entities/trainer-stats";
import {
  isGradingInProgress,
  trainerSessionQueryOptions,
  type TrainerSessionHistoryItem,
} from "@/entities/trainer-session";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { formatShortDateWithTime } from "@/shared/lib/date/format";
import { pluralize } from "@/shared/lib/pluralize";
import { Badge } from "@/shared/ui/kit/badge";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";

import { sortSessionsByRecency } from "../lib/sort-by-recency";

/**
 * История симуляций (#568) + сильные/слабые стороны (#614 H) на странице «Симуляция».
 * Сильные/слабые темы теперь стоят на ИЗМЕРЕННОМ per-topic mastery поверх всей активности
 * тренажёра (drill/learn/test/mock), а не на одном AI-разборе мока: тема попадает в колонки
 * только с достаточным числом ответов (бэк-порог), показывается mastery% + контекст объёма
 * выборки. AI-сигнал из моков остаётся как вторичная де-шумленная подсказка. История моков
 * (дата/балл/охват, клик → разбор) — без изменений. Own-data (auth-gated, см. HubMockTab).
 */
export function MockHistory() {
  const historyQuery = useQuery(trainerSessionQueryOptions.historyOptions({ mode: "MOCK" }));
  const strengthsQuery = useQuery(trainerStrengthsQueryOptions());

  if (historyQuery.isPending) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 2 }).map((_, index) => (
          <Skeleton key={index} className="h-20 w-full rounded-xl" />
        ))}
      </div>
    );
  }

  // newest-first по показываемому времени (completedAt ?? startedAt), а не по баллу (#664).
  const mocks = sortSessionsByRecency(historyQuery.data ?? []);
  const strengths = strengthsQuery.data;
  const strong = strengths?.strongTopics ?? [];
  const weak = strengths?.weakTopics ?? [];
  const sample = strengths?.sampleSize;
  const mockHints = strengths?.mockHintTopics ?? [];

  // Нечего показать вообще — секцию не плодим.
  const hasStrengthsData = (sample?.assessedTopics ?? 0) > 0 || mockHints.length > 0;
  if (mocks.length === 0 && !hasStrengthsData) {
    return null;
  }

  const scored = mocks.filter((session) => session.scorePercent !== null);
  const avgScore =
    scored.length > 0
      ? Math.round(
          scored.reduce((sum, session) => sum + (session.scorePercent ?? 0), 0) / scored.length,
        )
      : null;

  return (
    <section className="space-y-5">
      {strengthsQuery.isSuccess && (
        <StrengthsBlock strong={strong} weak={weak} sample={sample} mockHints={mockHints} />
      )}

      {mocks.length > 0 && (
        <div className="space-y-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h3 className="text-base font-semibold tracking-tight">История симуляций</h3>
            <p className="text-xs text-muted-foreground">
              {mocks.length} {pluralize(mocks.length, "симуляция", "симуляции", "симуляций")}
              {avgScore !== null && (
                <>
                  {" · средний балл "}
                  <span className="font-semibold text-foreground/80 tabular-nums">{avgScore}%</span>
                </>
              )}
            </p>
          </div>
          <ul className="space-y-2">
            {mocks.map((session) => (
              <MockRow key={session.id} session={session} />
            ))}
          </ul>
        </div>
      )}
    </section>
  );
}

/**
 * Блок «Сильные и слабые стороны» по измеренному mastery (#614 H). Заголовок отражает всю
 * активность тренажёра (не только собесы), под ним — строка объёма выборки. Пустое состояние —
 * дружелюбный nudge. AI-подсказки из моков — вторичный, явно отделённый блок.
 */
function StrengthsBlock({
  strong,
  weak,
  sample,
  mockHints,
}: {
  strong: TrainerStrengthTopic[];
  weak: TrainerStrengthTopic[];
  sample: TrainerStrengthSampleSize | undefined;
  mockHints: string[];
}) {
  return (
    <div className="space-y-3">
      <p className="text-base font-semibold tracking-tight">Сильные и слабые стороны</p>
      <TrainerStrengthsPanels strong={strong} weak={weak} sample={sample} mockHints={mockHints} />
    </div>
  );
}

/**
 * Истёк ли информативный таймер MOCK-сессии (сервер не авто-фейлит) — тогда
 * «продолжать» нечего, показываем «Посмотреть результаты». Вынесено из render
 * (Date.now — impure внутри компонента для React Compiler).
 */
function isSessionExpired(session: TrainerSessionHistoryItem): boolean {
  return (
    session.status === "IN_PROGRESS" &&
    session.timeLimitSeconds != null &&
    Date.now() - new Date(session.startedAt).getTime() > session.timeLimitSeconds * 1000
  );
}

/** Строка одной симуляции в истории: дата + охват + балл + отвечено; клик → разбор. */
function MockRow({ session }: { session: TrainerSessionHistoryItem }) {
  const completed = session.status === "COMPLETED";
  const score = session.scorePercent;
  const isExpired = isSessionExpired(session);
  // Симуляция завершена, но ИИ ещё разбирает открытые ответы (фон) — показываем это в истории,
  // чтобы вернувшийся на вкладку юзер видел, что проверка идёт (#568). Приоритет над баллом.
  const isGrading = isGradingInProgress(session.gradingStatus);

  return (
    <li>
      <Link
        href={routes.trainerSession(session.id)}
        className="flex items-center gap-3 rounded-xl border border-border/60 bg-card p-4 transition-colors hover:border-border hover:bg-accent/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:outline-none"
      >
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">Симуляция собеседования</p>
          <p className="mt-0.5 text-xs text-muted-foreground tabular-nums">
            {formatShortDateWithTime(session.completedAt ?? session.startedAt)} ·{" "}
            {session.answeredCount}/{session.totalCount} отвечено
          </p>
        </div>
        <div className="shrink-0 text-right">
          {isGrading ? (
            <Badge variant="outline" className="gap-1 text-xs text-primary">
              <Icons.loading className="size-3 animate-spin" />
              ИИ проверяет…
            </Badge>
          ) : completed && score !== null ? (
            <>
              <p
                className={cn(
                  "text-lg font-semibold tabular-nums",
                  score >= 70 ? "text-green" : "text-foreground",
                )}
              >
                {score}%
              </p>
              <p className="text-[11px] text-muted-foreground">балл</p>
            </>
          ) : isExpired ? (
            <Badge variant="outline" className="text-xs text-muted-foreground">
              Посмотреть результаты
            </Badge>
          ) : (
            <Badge variant="outline" className="text-xs text-muted-foreground">
              Продолжить
            </Badge>
          )}
        </div>
      </Link>
    </li>
  );
}
