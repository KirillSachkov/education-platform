"use client";

import {
  reviewSubmissionsQueryOptions,
  type SubmissionAttemptHistoryItemDto,
} from "@/entities/review-submission";
import { Icons } from "@/shared/ui/icons";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { AiReviewHistory } from "./ai-review-history";

interface AttemptHistoryProps {
  submissionId: string;
  currentAttemptNumber: number;
}

function formatDate(value: string | null) {
  if (!value) return null;
  return new Date(value).toLocaleString("ru-RU", {
    day: "numeric",
    month: "long",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function AttemptHistory({
  submissionId,
  currentAttemptNumber,
}: AttemptHistoryProps) {
  const [expanded, setExpanded] = useState(false);
  const query = useQuery({
    ...reviewSubmissionsQueryOptions.attemptHistory(submissionId),
    enabled: expanded,
  });

  const previousAttemptsCount = currentAttemptNumber - 1;

  return (
    <details
      className="rounded-md border border-muted/50"
      onToggle={(event) =>
        setExpanded((event.target as HTMLDetailsElement).open)
      }
    >
      <summary className="cursor-pointer list-none px-4 py-2.5 text-sm font-medium text-muted-foreground hover:bg-accent/30 transition-colors">
        <span className="inline-flex items-center gap-2">
          <Icons.clock size={16} className="text-muted-foreground" />
          Предыдущие попытки ({previousAttemptsCount})
        </span>
      </summary>
      <div className="border-t border-muted/50 p-4">
        {!expanded ? null : query.isPending ? (
          <p className="flex items-center gap-2 text-sm text-muted-foreground">
            <Icons.loading size={16} className="animate-spin" /> Загружаем…
          </p>
        ) : query.isError ? (
          <p className="text-sm text-red">
            Не удалось загрузить историю попыток.
          </p>
        ) : (
          <Timeline
            attempts={query.data ?? []}
            currentAttemptNumber={currentAttemptNumber}
          />
        )}
      </div>
    </details>
  );
}

function Timeline({
  attempts,
  currentAttemptNumber,
}: {
  attempts: SubmissionAttemptHistoryItemDto[];
  currentAttemptNumber: number;
}) {
  const prior = attempts
    .filter((a) => a.attemptNumber < currentAttemptNumber)
    .sort((a, b) => b.attemptNumber - a.attemptNumber);

  if (prior.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">Прошлых попыток нет.</p>
    );
  }

  return (
    <ol className="space-y-3">
      {prior.map((attempt) => (
        <li
          key={attempt.submissionId}
          className="rounded-md border border-muted/40 p-3"
        >
          <div className="flex items-center gap-2 flex-wrap mb-1.5">
            <span className="text-sm font-semibold">
              Попытка #{attempt.attemptNumber}
            </span>
            <StatusBadge status={attempt.reviewStatus} />
            <span className="text-xs text-muted-foreground">
              {formatDate(attempt.reviewedAt ?? attempt.submittedAt)}
            </span>
          </div>
          <a
            href={attempt.payload}
            target="_blank"
            rel="noopener noreferrer"
            className="text-xs text-teal hover:underline inline-flex items-center gap-1 break-all"
          >
            <Icons.externalLink size={11} /> {attempt.payload}
          </a>
          {attempt.feedback && (
            <div className="mt-2 p-2.5 rounded bg-muted/40 text-sm whitespace-pre-wrap">
              <span className="font-medium text-foreground">
                Обратная связь:
              </span>{" "}
              <span className="text-muted-foreground">{attempt.feedback}</span>
            </div>
          )}
          {attempt.aiIterationsCount > 0 || attempt.aiReviewStatus != null ? (
            <div className="mt-2">
              <AiReviewHistory submissionId={attempt.submissionId} />
            </div>
          ) : null}
        </li>
      ))}
    </ol>
  );
}
