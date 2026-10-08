"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import {
  CheckCircle2,
  Circle,
  Clock3,
  GitPullRequest,
  ListChecks,
  MessageSquareWarning,
  PlayCircle,
} from "lucide-react";
import type {
  IssueSubmissionHistoryDto,
  IssueSubmissionHistoryItemDto,
  SubmissionReviewStatus,
} from "@/entities/course-progress";
import { formatFullDateWithTime } from "@/shared/lib/date";
import { CardTitle } from "@/shared/ui/kit/card";
import { cn } from "@/shared/lib/css";

type StepTone = "yellow" | "primary" | "blue" | "red" | "teal" | "muted";

interface TimelineEntry {
  key: string;
  title: ReactNode;
  content: ReactNode;
  icon: typeof Circle;
  tone: StepTone;
}

interface IssueSubmissionProgressProps {
  history: IssueSubmissionHistoryDto | null;
}

function formatDate(value: string | null) {
  if (!value) {
    return null;
  }

  return formatFullDateWithTime(value);
}

function getToneClasses(tone: StepTone) {
  switch (tone) {
    case "yellow":
      return {
        dot: "border-yellow/20 bg-yellow/10 text-yellow",
        line: "bg-yellow/70",
        title: "text-yellow",
      };
    case "primary":
      return {
        dot: "border-primary/20 bg-primary/10 text-primary",
        line: "bg-primary/70",
        title: "text-primary",
      };
    case "blue":
      return {
        dot: "border-blue/20 bg-blue/10 text-blue",
        line: "bg-blue/70",
        title: "text-blue",
      };
    case "red":
      return {
        dot: "border-red/20 bg-red/10 text-red",
        line: "bg-red/70",
        title: "text-red",
      };
    case "teal":
      return {
        dot: "border-green/20 bg-green/10 text-green",
        line: "bg-green/70",
        title: "text-green",
      };
    default:
      return {
        dot: "border-border bg-card text-muted-foreground",
        line: "bg-border/80",
        title: "text-foreground/85",
      };
  }
}

function getReviewEntry(
  attempt: IssueSubmissionHistoryItemDto,
): TimelineEntry | null {
  const reviewStartedLabel = formatDate(attempt.reviewStartedAt);
  const reviewedLabel = formatDate(attempt.reviewedAt);
  const feedback = attempt.feedback?.trim();

  switch (attempt.reviewStatus as SubmissionReviewStatus) {
    case "PENDING":
      return {
        key: `review-pending-${attempt.submissionId}`,
        title: "Ожидает проверки",
        content: (
          <span>
            Решение отправлено и ожидает начала проверки
            {reviewedLabel ? ` с ${reviewedLabel}` : "."}
          </span>
        ),
        icon: Clock3,
        tone: "blue",
      };
    case "IN_REVIEW":
      return {
        key: `review-in-progress-${attempt.submissionId}`,
        title: "На проверке",
        content: (
          <span>
            {reviewStartedLabel
              ? `Проверка начата ${reviewStartedLabel}.`
              : "Решение передано на проверку."}
          </span>
        ),
        icon: Clock3,
        tone: "blue",
      };
    case "CHANGES_REQUESTED":
      return {
        key: `review-changes-${attempt.submissionId}`,
        title: "Запрошены правки",
        content: (
          <span>
            {feedback ||
              (reviewedLabel
                ? `Ревью завершено ${reviewedLabel}. Требуются доработки.`
                : "Ревьюер вернул задачу на доработку.")}
          </span>
        ),
        icon: MessageSquareWarning,
        tone: "red",
      };
    case "APPROVED":
      return {
        key: `review-approved-${attempt.submissionId}`,
        title: "Успешно выполнено",
        content: (
          <span>
            {feedback ||
              (reviewedLabel
                ? `Задача завершена ${reviewedLabel}.`
                : "Задача успешно завершена.")}
          </span>
        ),
        icon: CheckCircle2,
        tone: "teal",
      };
    default:
      return null;
  }
}

function buildEntries(history: IssueSubmissionHistoryDto | null): TimelineEntry[] {
  if (!history || (history.currentStatus === "NOT_STARTED" && history.attempts.length === 0)) {
    return [
      {
        key: "idle",
        title: "Работа ещё не начата",
        content: "Когда вы возьмёте задачу в работу, история выполнения появится здесь.",
        icon: Circle,
        tone: "muted",
      },
    ];
  }

  const entries: TimelineEntry[] = [];
  const startedLabel = formatDate(history.startedAt);

  if (history.startedAt) {
    entries.push({
      key: "work",
      title: "Задача взята в работу",
      content: startedLabel
        ? `Работа начата ${startedLabel}.`
        : "Задача активирована и готова к выполнению.",
      icon: PlayCircle,
      tone: "yellow",
    });
  }

  for (const attempt of history.attempts) {
    const submittedLabel = formatDate(attempt.submittedAt);

    entries.push({
      key: `submitted-${attempt.submissionId}`,
      title: "Решение отправлено",
      content: (
        <span className="break-all">
          <Link
            href={attempt.payload}
            target="_blank"
            rel="noreferrer"
            className="underline decoration-border underline-offset-4 hover:text-foreground"
          >
            {attempt.payload}
          </Link>
          {submittedLabel ? ` • ${submittedLabel}` : null}
        </span>
      ),
      icon: GitPullRequest,
      tone: "primary",
    });

    const reviewEntry = getReviewEntry(attempt);
    if (reviewEntry) {
      entries.push(reviewEntry);
    }
  }

  if (entries.length === 0) {
    return [
      {
        key: "idle",
        title: "Работа ещё не начата",
        content: "Когда вы возьмёте задачу в работу, история выполнения появится здесь.",
        icon: Circle,
        tone: "muted",
      },
    ];
  }

  return entries;
}

export function IssueSubmissionProgress({
  history,
}: IssueSubmissionProgressProps) {
  const entries = buildEntries(history);

  return (
    <section className="p-5">
      <CardTitle className="mb-4 flex items-center gap-2 text-sm">
        <ListChecks size={14} className="text-primary" />
        Ход выполнения
      </CardTitle>

      <div className="space-y-0">
        {entries.map((entry, index) => {
          const Icon = entry.icon;
          const classes = getToneClasses(entry.tone);
          const isLast = index === entries.length - 1;
          const nextEntry = isLast ? null : entries[index + 1];
          const nextClasses = getToneClasses(nextEntry?.tone ?? entry.tone);
          // Celebratory state: the latest entry is "APPROVED" (success tone)
          const isCelebratory = isLast && entry.tone === "teal";

          return (
            <div key={entry.key} className="flex gap-3">
              <div className="flex flex-col items-center">
                <div className="relative">
                  {isCelebratory && (
                    <div
                      className="absolute -inset-2 rounded-full bg-green/30 blur-lg animate-soft-float pointer-events-none"
                      aria-hidden="true"
                    />
                  )}
                  <div
                    className={cn(
                      "relative flex items-center justify-center rounded-full border",
                      classes.dot,
                      isCelebratory
                        ? "size-10 shadow-[0_0_16px] shadow-green/50"
                        : "size-7",
                    )}
                  >
                    <Icon size={isCelebratory ? 20 : 14} />
                  </div>
                </div>
                {!isLast && (
                  <div
                    className={cn("mt-1 h-full min-h-8 w-px", nextClasses.line)}
                  />
                )}
              </div>

              <div className="relative min-w-0 pb-5 flex-1">
                {isCelebratory && (
                  <div
                    className="absolute inset-0 pointer-events-none overflow-hidden rounded-lg"
                    aria-hidden="true"
                  >
                    <div
                      className="absolute inset-y-0 -left-1/2 w-1/3 animate-shimmer-sweep"
                      style={{
                        background:
                          "linear-gradient(100deg, transparent, color-mix(in oklch, var(--green) 25%, transparent), transparent)",
                      }}
                    />
                  </div>
                )}
                <p
                  className={cn(
                    "relative text-sm font-semibold",
                    classes.title,
                    isCelebratory && "text-base",
                  )}
                >
                  {entry.title}
                </p>
                <div className="relative mt-1 text-sm leading-6 text-muted-foreground">
                  {entry.content}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}
