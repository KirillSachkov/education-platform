"use client";

import {
  isGradingInProgress,
  trainerSessionQueryOptions,
  type TrainerSessionBreakdown,
  type TrainerSessionItem,
} from "@/entities/trainer-session";
import { trainerTopicsQueryOptions } from "@/entities/trainer-topic";
import { routes } from "@/shared/config/routes";
import { TRAINER_DIFFICULTY_VISUALS, getMasteryTone } from "@/shared/config/trainer";
import { cn } from "@/shared/lib/css";
import { isTrainerContentRedacted } from "@/shared/lib/trainer-redaction";
import { LockCallout, LockedContentPlaceholder } from "@/shared/ui/components";
import { MarkdownContent } from "@/shared/ui/components/markdown-content";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { DrillAnswerReview, revealFromItem } from "./drill-answer-review";

interface MockResultsCardProps {
  sessionId: string;
}

/**
 * Итог MOCK-сессии (#568 + AI-грейдинг #585): крупный балл + разбивка по
 * сложности/темам. Открытые ответы проверяет ИИ асинхронно — пока
 * `gradingStatus` PENDING/GRADING показываем спокойный лоадер и поллим сессию
 * (поллинг встроен в `sessionOptions`); на GRADED раскрываем пер-вопросный разбор
 * + блок «Разбор ИИ» (слабые темы / сильные стороны). FAILED — авто-балл без
 * AI-фидбэка. Зеркалит курсовую статистику тестов платформы.
 */
export function MockResultsCard({ sessionId }: MockResultsCardProps) {
  // Поллит, пока идёт AI-грейдинг (refetchInterval в sessionOptions).
  const sessionQuery = useQuery(trainerSessionQueryOptions.sessionOptions(sessionId));
  const statsQuery = useQuery(trainerSessionQueryOptions.statsOptions(sessionId));
  const topicsQuery = useQuery(trainerTopicsQueryOptions.topicsOptions());

  const gradingStatus = sessionQuery.data?.gradingStatus;
  const isGrading = isGradingInProgress(gradingStatus);

  if (statsQuery.isPending || sessionQuery.isPending) {
    return (
      <div className="mx-auto w-full max-w-3xl space-y-4">
        <Skeleton className="h-40 w-full rounded-2xl" />
        <Skeleton className="h-32 w-full rounded-2xl" />
      </div>
    );
  }

  // Stats/сессия недоступны (редко) — деградируем до простого CTA, не падаем.
  if (statsQuery.isError || !statsQuery.data || sessionQuery.isError || !sessionQuery.data) {
    return (
      <div className="mx-auto w-full max-w-3xl space-y-4 text-center">
        <p className="text-sm text-muted-foreground">Симуляция завершена.</p>
        <Button asChild>
          <Link href={routes.trainerSession(sessionId)}>Открыть разбор</Link>
        </Button>
      </div>
    );
  }

  const stats = statsQuery.data;
  const session = sessionQuery.data;
  const score = stats.scorePercent ?? 0;
  const strong = score >= 70;
  const topicNames = new Map((topicsQuery.data ?? []).map((topic) => [topic.id, topic.title]));

  const difficultyOrder = ["JUNIOR", "MIDDLE", "SENIOR"];
  const byDifficulty = [...stats.byDifficulty].sort(
    (a, b) => difficultyOrder.indexOf(a.key) - difficultyOrder.indexOf(b.key),
  );

  // Пока ИИ проверяет открытые ответы — спокойный лоадер вместо итога.
  if (isGrading) {
    return (
      <div className="mx-auto w-full max-w-3xl">
        <div className="flex flex-col items-center gap-3 rounded-xl border border-border/60 bg-card px-6 py-12 text-center">
          <span className="flex size-12 items-center justify-center rounded-full bg-primary/10">
            <Icons.ai className="size-6 text-primary" />
          </span>
          <div className="space-y-1">
            <p className="flex items-center justify-center gap-2 font-medium">
              <Icons.loading className="size-4 animate-spin text-muted-foreground" />
              ИИ проверяет ответы…
            </p>
            <p className="text-sm text-muted-foreground">
              Разбираем твои открытые ответы и готовим фидбэк. Это займёт около минуты — страница
              обновится сама.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-3xl space-y-5">
      <div className="overflow-hidden rounded-xl border border-border/60 bg-card">
        <div
          className={cn(
            "flex flex-col items-center gap-1.5 px-6 py-9 text-center",
            strong ? "bg-green/5" : "bg-muted/30",
          )}
        >
          <p className="text-5xl font-semibold tabular-nums">{score}%</p>
          <p className="text-sm text-muted-foreground">
            {strong ? "Сильная симуляция!" : "Симуляция собеседования завершена"}
          </p>
        </div>

        <dl className="grid grid-cols-3 divide-x divide-border/60 border-t border-border/60 text-center">
          <Stat label="Вопросов" value={stats.totalItems} />
          <Stat label="Отвечено" value={stats.answeredItems} />
          <Stat label="Верно" value={stats.correctItems} />
        </dl>
      </div>

      {/* Разбор ИИ — общий фидбэк + слабые темы + сильные стороны. */}
      {gradingStatus === "GRADED" &&
        (session.aiOverallFeedback ||
          session.aiWeakTopics.length > 0 ||
          session.aiStrengths.length > 0) && (
          <section className="space-y-3 rounded-2xl border border-primary/30 bg-primary/5 p-4 sm:p-5">
            <h3 className="flex items-center gap-2 text-sm font-semibold tracking-tight">
              <Icons.ai className="size-4 text-primary" />
              Разбор ИИ
            </h3>
            {session.aiOverallFeedback && (
              <p className="whitespace-pre-wrap text-sm leading-relaxed text-foreground/90">
                {session.aiOverallFeedback}
              </p>
            )}
            <div className="grid gap-3 sm:grid-cols-2">
              <TagList
                title="Слабые темы"
                items={session.aiWeakTopics}
                tone="warn"
                icon={<Icons.target className="size-3.5" />}
              />
              <TagList
                title="Сильные стороны"
                items={session.aiStrengths}
                tone="good"
                icon={<Icons.completed className="size-3.5" />}
              />
            </div>
          </section>
        )}

      {/* AI недоступен — показываем авто-балл, но честно говорим про фидбэк. */}
      {gradingStatus === "FAILED" && (
        <p className="flex items-start gap-2 rounded-xl border border-amber-500/40 bg-amber-500/5 p-3 text-sm text-foreground/80">
          <Icons.warning className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>
            ИИ-разбор открытых ответов сейчас недоступен. Балл по авто-проверяемым вопросам показан
            выше — ответы и эталоны можно сверить ниже.
          </span>
        </p>
      )}

      {byDifficulty.length > 0 && (
        <BreakdownSection title="По уровням сложности">
          {byDifficulty.map((row) => (
            <BreakdownRow
              key={row.key}
              label={TRAINER_DIFFICULTY_VISUALS[row.key]?.label ?? row.key}
              labelClass={TRAINER_DIFFICULTY_VISUALS[row.key]?.badgeClass}
              row={row}
            />
          ))}
        </BreakdownSection>
      )}

      {stats.byTopic.length > 0 && (
        <BreakdownSection title="По темам">
          {stats.byTopic.map((row) => (
            <BreakdownRow key={row.key} label={topicNames.get(row.key) ?? "Тема"} row={row} />
          ))}
        </BreakdownSection>
      )}

      {/* Пер-вопросный разбор — здесь грейдинг уже терминальный (PENDING/GRADING
          вернули лоадер выше). */}
      <MockAnswersReview items={session.items} />

      <div className="flex flex-col gap-2 sm:flex-row">
        <Button asChild variant="outline" className="flex-1">
          <Link href={routes.trainerSession(sessionId)}>Разбор ответов</Link>
        </Button>
        <Button asChild className="flex-1">
          <Link href={routes.trainer}>К тренажёру</Link>
        </Button>
      </div>
    </div>
  );
}

/**
 * Пер-вопросный разбор мок-сессии: текст вопроса + ответ/вердикт/эталон
 * (`DrillAnswerReview`) + балл и AI-фидбэк (если есть). Пропущенные вопросы
 * помечаем «без ответа».
 */
function MockAnswersReview({ items }: { items: TrainerSessionItem[] }) {
  return (
    <section className="space-y-3">
      <h3 className="text-base font-semibold tracking-tight">Разбор ответов</h3>
      {items.map((item, idx) => {
        const difficultyVisual = item.difficulty
          ? TRAINER_DIFFICULTY_VISUALS[item.difficulty]
          : null;
        return (
          <div key={item.id} className="rounded-xl border border-border/60 bg-card p-4 sm:p-5">
            <div className="mb-3 flex items-center justify-between gap-2">
              <span className="text-xs font-medium text-muted-foreground">Вопрос {idx + 1}</span>
              <div className="flex shrink-0 items-center gap-1.5">
                {item.scorePercent != null && (
                  <span className="font-mono text-xs tabular-nums text-muted-foreground">
                    {item.scorePercent}%
                  </span>
                )}
                {difficultyVisual && (
                  <span
                    className={cn(
                      "inline-flex items-center rounded-md px-2 py-0.5 text-xs font-medium",
                      difficultyVisual.badgeClass,
                    )}
                  >
                    {difficultyVisual.label}
                  </span>
                )}
              </div>
            </div>
            {isTrainerContentRedacted(item.isLocked, item.questionText) ? (
              <LockedContentPlaceholder lines={2} />
            ) : (
              <MarkdownContent
                variant="compact"
                disableLinks
                className="[&>*:first-child]:mt-0 [&>*:last-child]:mb-0 [&_pre]:overflow-x-auto [&_pre_code]:!whitespace-pre"
              >
                {item.questionText as string}
              </MarkdownContent>
            )}
            <div className="mt-4">
              {item.isLocked ? (
                <LockCallout
                  reason={item.lockReason ?? "pro_required"}
                  ctaHref={routes.trainerPro}
                />
              ) : item.isAnswered ? (
                // AI-разбор открытого ответа (#585) рендерит сам DrillAnswerReview (блок «Разбор ИИ»).
                <DrillAnswerReview item={item} revealed={revealFromItem(item)} />
              ) : (
                <p className="text-sm text-muted-foreground">Вопрос остался без ответа.</p>
              )}
            </div>
          </div>
        );
      })}
    </section>
  );
}

function TagList({
  title,
  items,
  tone,
  icon,
}: {
  title: string;
  items: string[];
  tone: "warn" | "good";
  icon: React.ReactNode;
}) {
  if (items.length === 0) return null;
  return (
    <div className="space-y-1.5">
      <p className="flex items-center gap-1.5 text-xs font-medium text-muted-foreground">
        {icon}
        {title}
      </p>
      <ul className="flex flex-wrap gap-1.5">
        {items.map((entry) => (
          <li
            key={entry}
            className={cn(
              "rounded-md px-2 py-0.5 text-xs font-medium",
              tone === "warn"
                ? "bg-amber-500/10 text-amber-700 dark:text-amber-400"
                : "bg-green/10 text-green",
            )}
          >
            {entry}
          </li>
        ))}
      </ul>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div className="px-2 py-4">
      <dd className="text-xl font-semibold tabular-nums">{value}</dd>
      <dt className="text-xs text-muted-foreground">{label}</dt>
    </div>
  );
}

function BreakdownSection({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2 rounded-2xl border border-border/60 bg-card p-4 sm:p-5">
      <h3 className="text-sm font-semibold tracking-tight">{title}</h3>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

function BreakdownRow({
  label,
  labelClass,
  row,
}: {
  label: string;
  labelClass?: string;
  row: TrainerSessionBreakdown;
}) {
  // Доля считается от авто-грейдимых (graded); если их нет (всё OPEN_TEXT) — показываем «—».
  const hasGraded = row.graded > 0;
  const percent = hasGraded ? Math.round((row.correct / row.graded) * 100) : 0;
  const tone = getMasteryTone(percent);

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between gap-2">
        {labelClass ? (
          <span
            className={cn("inline-flex rounded-md px-2 py-0.5 text-xs font-medium", labelClass)}
          >
            {label}
          </span>
        ) : (
          <span className="min-w-0 truncate text-sm font-medium" title={label}>
            {label}
          </span>
        )}
        <span className="shrink-0 font-mono text-xs tabular-nums text-muted-foreground">
          {hasGraded ? `${row.correct}/${row.graded} · ${percent}%` : `${row.total} откр.`}
        </span>
      </div>
      {hasGraded && (
        <div className="h-1.5 overflow-hidden rounded-full bg-border/50">
          <div
            className={cn("h-full rounded-full transition-[width]", tone)}
            style={{ width: `${Math.max(percent, 2)}%` }}
          />
        </div>
      )}
    </div>
  );
}
