"use client";

import type { AiReviewIterationDto } from "../types";
import { resolveIterationFailureCopy } from "../failure-copy";
import { VERDICT_LABEL, VERDICT_TONE } from "../verdict-config";
import { formatRelativeDate } from "@/shared/lib/date";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import type { ReactNode } from "react";

interface IterationCardProps {
  iteration: AiReviewIterationDto;
  pullRequestUrl: string;
  /** Marks the most recent iteration — gets a brighter ring + "Последняя" tag. */
  isLatest?: boolean;
  /**
   * Optional slot для feedback-кнопок (👍/👎). Передаётся консьюмером
   * (feature/widget слой), потому что entities/ не может импортировать
   * из features/ — FSD boundaries.
   */
  feedbackSlot?: ReactNode;
}

export function IterationCard({
  iteration,
  pullRequestUrl,
  isLatest = false,
  feedbackSlot,
}: IterationCardProps) {
  const isFailed = iteration.status === "FAILED";
  const isRunning = iteration.status === "PROCESSING";
  // «PR слишком большой» — не ошибка, а инструкция: автор может запустить
  // AI-проверку вручную (бэкенд разобьёт PR на части). Рендерим спокойным
  // amber-стейтом, а не красным error-treatment'ом.
  const isTooLarge = isFailed && iteration.failureReason === "review.diff.too_large";
  const tone = iteration.verdict ? VERDICT_TONE[iteration.verdict] : null;

  const timeLabel = formatRelativeDate(iteration.completedAt ?? iteration.startedAt);

  return (
    <article
      className={cn(
        "group/iter relative rounded-2xl border bg-card p-4 transition-shadow",
        "shadow-sm hover:shadow-md",
        isLatest ? "border-border ring-1 ring-primary/15" : "border-border/60",
      )}
      data-testid="iteration-card"
    >
      {/* Header: number + verdict pill */}
      <header className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2.5">
          <span
            className={cn(
              "inline-flex size-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold tabular-nums",
              isTooLarge
                ? "bg-yellow-dim text-yellow"
                : isFailed
                  ? "bg-red-dim text-red"
                  : "bg-muted text-muted-foreground",
            )}
            aria-hidden
          >
            {iteration.iterationNumber}
          </span>
          <div className="flex flex-col gap-0.5">
            <span className="text-sm font-medium leading-none text-foreground">
              Итерация #{iteration.iterationNumber}
            </span>
            {isLatest ? (
              <span className="text-[11px] font-medium uppercase tracking-wide text-primary/80">
                Последняя
              </span>
            ) : null}
          </div>
        </div>

        {iteration.verdict && tone ? (
          <Badge
            variant="outline"
            className={cn("shrink-0 font-medium", tone.pill)}
            data-testid="iteration-verdict"
          >
            {VERDICT_LABEL[iteration.verdict]}
          </Badge>
        ) : isRunning ? (
          <Badge variant="outline" className="shrink-0 gap-1.5 border-blue/30 bg-blue/10 text-blue">
            <Icons.loading className="size-3 animate-spin" />
            Проверяется
          </Badge>
        ) : isTooLarge ? (
          <Badge
            variant="outline"
            className="shrink-0 gap-1 border-yellow-muted bg-yellow-dim text-yellow"
          >
            <Icons.info className="size-3" />
            Большой PR
          </Badge>
        ) : isFailed ? (
          <Badge variant="outline" className="shrink-0 gap-1 border-red-muted bg-red-dim text-red">
            <Icons.warning className="size-3" />
            Ошибка
          </Badge>
        ) : null}
      </header>

      {/* Meta line: relative time + model */}
      <div className="mt-2 flex flex-wrap items-center gap-x-2.5 gap-y-1 text-xs text-muted-foreground">
        <span className="inline-flex items-center gap-1">
          <Icons.clock className="size-3" />
          {timeLabel}
        </span>
        {iteration.modelUsed ? (
          <>
            <span aria-hidden className="text-border">
              ·
            </span>
            <span className="inline-flex items-center gap-1 font-mono text-[11px]">
              {iteration.modelUsed}
            </span>
          </>
        ) : null}
      </div>

      {/* Failure reason — red error, except «too large» which is a calm amber note */}
      {isFailed && iteration.failureReason ? (
        <p
          className={cn(
            "mt-3 rounded-lg border px-3 py-2 text-sm",
            isTooLarge
              ? "border-yellow-muted bg-yellow-dim text-yellow"
              : "border-red-muted bg-red-dim text-red",
          )}
        >
          {resolveIterationFailureCopy(iteration.failureReason)}
        </p>
      ) : null}

      {/* Short summary — shown in full, no truncation */}
      {iteration.summary ? (
        <p className="mt-3 whitespace-pre-wrap text-sm leading-relaxed text-foreground/90">
          {iteration.summary}
        </p>
      ) : null}

      {/* Footer: inline-comments count + PR review link */}
      <footer className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-border/50 pt-3 text-xs">
        <span className="inline-flex items-center gap-1.5 text-muted-foreground">
          <Icons.comment className="size-3.5" />
          {iteration.inlineCommentsCount > 0
            ? `${iteration.inlineCommentsCount} inline-комментариев`
            : "Inline-комментариев нет"}
        </span>
        {iteration.gitHubReviewId ? (
          <a
            href={`${pullRequestUrl}#pullrequestreview-${iteration.gitHubReviewId}`}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-1 font-medium text-primary transition-colors hover:text-primary/80 hover:underline"
            data-testid="iteration-pr-link"
          >
            Открыть в PR
            <Icons.externalLink className="size-3.5" />
          </a>
        ) : null}
      </footer>

      {!isFailed && feedbackSlot ? (
        <div className="mt-3 flex justify-end">{feedbackSlot}</div>
      ) : null}
    </article>
  );
}
