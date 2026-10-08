"use client";

import {
  aiReviewBySubmissionQueryOptions,
  IterationTimeline,
  useCancelAiReview,
  useRestartAiReview,
  VERDICT_LABEL,
  VERDICT_TONE,
  type AiReviewDetailDto,
  type AiReviewStatus,
} from "@/entities/ai-review";
import { isEnvelopeError } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/shared/ui/kit/collapsible";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useRequestAiReview } from "../model/use-request-ai-review";
import { StudentQuestionThread } from "./student-question-thread";

interface AiReviewHistoryProps {
  submissionId: string;
}

/**
 * Phase 10 (#15) — Автор-side disclosure: collapsed by default, на раскрытии
 * подгружает все AI iteration'ы. Использует тот же queryOptions что и студент-side
 * AiReviewBlock — TanStack кэш переиспользуется при переходах между ролями.
 *
 * Автор — escape hatch: может перепроверить PR (re-run AI) даже если студент
 * этого не делал.
 */
export function AiReviewHistory({ submissionId }: AiReviewHistoryProps) {
  const [open, setOpen] = useState(false);
  const [forceStartRequested, setForceStartRequested] = useState(false);
  const query = useQuery({
    ...aiReviewBySubmissionQueryOptions(submissionId),
    enabled: open,
    refetchInterval: (q) => (forceStartRequested && q.state.status === "error" ? 3_000 : false),
  });

  return (
    <Collapsible
      open={open}
      onOpenChange={setOpen}
      className="overflow-hidden rounded-xl border border-border/60 bg-muted/20"
    >
      <CollapsibleTrigger
        className={cn(
          "group/hist flex w-full items-center gap-2 px-4 py-2.5 text-sm font-medium",
          "text-muted-foreground transition-colors hover:bg-accent/40 hover:text-foreground",
        )}
        data-testid="ai-history-toggle"
      >
        <span className="inline-flex size-6 items-center justify-center rounded-md bg-primary/10 text-primary">
          <Icons.ai className="size-3.5" />
        </span>
        AI-история ревью
        <Icons.chevronDown className="ml-auto size-4 transition-transform duration-200 group-data-[state=open]/hist:rotate-180" />
      </CollapsibleTrigger>

      <CollapsibleContent className="overflow-hidden data-[state=closed]:animate-collapsible-up data-[state=open]:animate-collapsible-down">
        <div className="border-t border-border/60 p-4">
          {query.isPending ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <Icons.loading className="size-4 animate-spin" /> Загружаем историю…
            </p>
          ) : query.isError ? (
            <NoHistory
              error={query.error}
              submissionId={submissionId}
              onForceStartRequested={() => setForceStartRequested(true)}
            />
          ) : (
            <HistoryView review={query.data} submissionId={submissionId} />
          )}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}

function NoHistory({
  error,
  submissionId,
  onForceStartRequested,
}: {
  error: Error;
  submissionId: string;
  onForceStartRequested: () => void;
}) {
  const code = isEnvelopeError(error) ? error.messages[0]?.code : undefined;
  if (code === "review.not_found") {
    // #383 — для этой попытки AI ещё не создавался (auto-review был выключен / не было
    // GitHub-инсталляции в момент сабмита). Автор/админ может форсить проверку.
    return <ForceStartAi submissionId={submissionId} onStarted={onForceStartRequested} />;
  }
  return (
    <p className="rounded-lg border border-red-muted bg-red-dim px-3 py-2 text-sm text-red">
      Не удалось загрузить AI-историю.
    </p>
  );
}

function ForceStartAi({
  submissionId,
  onStarted,
}: {
  submissionId: string;
  onStarted: () => void;
}) {
  const mutation = useRequestAiReview(submissionId, { onStarted });
  return (
    <div className="flex flex-col items-start gap-3">
      <p className="text-sm text-muted-foreground">AI не запускалась для этой попытки.</p>
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => mutation.mutate()}
        disabled={mutation.isPending}
        data-testid="force-start-ai-button"
      >
        {mutation.isPending ? (
          <>
            <Icons.loading className="mr-1.5 size-4 animate-spin" />
            Запускаем…
          </>
        ) : (
          <>
            <Icons.ai className="mr-1.5 size-4" />
            Запустить AI
          </>
        )}
      </Button>
    </div>
  );
}

const STATUS_LABEL: Record<AiReviewStatus, string> = {
  QUEUED: "В очереди",
  RUNNING: "Проверяется",
  READY: "Завершена",
  FAILED: "Ошибка",
};

function HistoryView({
  review,
  submissionId,
}: {
  review: AiReviewDetailDto;
  submissionId: string;
}) {
  const restartMutation = useRestartAiReview({ reviewId: review.id, submissionId });
  const cancelMutation = useCancelAiReview({ reviewId: review.id, submissionId });
  const isActive = review.status === "QUEUED" || review.status === "RUNNING";
  const isMutating = restartMutation.isPending || cancelMutation.isPending;
  const latestTone = review.latestVerdict ? VERDICT_TONE[review.latestVerdict] : null;

  const handleRestart = () => {
    const confirmed = window.confirm(
      isActive
        ? "Остановить текущую AI-проверку и запустить новую?"
        : "Запустить новую AI-проверку для этой попытки?",
    );
    if (confirmed) restartMutation.mutate();
  };

  const handleCancel = () => {
    const confirmed = window.confirm("Остановить текущую AI-проверку?");
    if (confirmed) cancelMutation.mutate();
  };

  return (
    <div className="space-y-4">
      {/* Meta strip */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg bg-card px-3 py-2.5 text-xs">
        <span className="inline-flex items-center gap-1.5 font-medium text-foreground">
          <Icons.github className="size-3.5 text-muted-foreground" />
          <span className="font-mono">{review.repoFullName}</span>
          <span className="text-muted-foreground">· PR #{review.pullNumber}</span>
        </span>
        <span aria-hidden className="text-border">
          |
        </span>
        <span className="text-muted-foreground">
          {STATUS_LABEL[review.status]} · {review.iterationsCount} итер. · обновлено{" "}
          {new Date(review.updatedAt).toLocaleString("ru-RU")}
        </span>
        {review.latestVerdict && latestTone ? (
          <Badge variant="outline" className={cn("ml-auto font-medium", latestTone.pill)}>
            {VERDICT_LABEL[review.latestVerdict]}
          </Badge>
        ) : null}
      </div>

      {review.iterations.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Студент ещё не запускал ни одной AI-итерации.
        </p>
      ) : (
        <IterationTimeline iterations={review.iterations} pullRequestUrl={review.pullRequestUrl} />
      )}

      <StudentQuestionThread submissionId={submissionId} messages={review.studentMessages} />

      <div className="flex flex-wrap justify-end gap-2 border-t border-border/50 pt-3">
        {isActive ? (
          <Button
            type="button"
            size="sm"
            variant="destructive"
            onClick={handleCancel}
            disabled={isMutating}
            data-testid="author-cancel-ai-button"
          >
            {cancelMutation.isPending ? (
              <>
                <Icons.loading className="mr-1.5 size-4 animate-spin" />
                Останавливаем…
              </>
            ) : (
              <>
                <Icons.stop className="mr-1.5 size-4" />
                Остановить
              </>
            )}
          </Button>
        ) : null}
        <Button
          type="button"
          size="sm"
          variant="outline"
          onClick={handleRestart}
          disabled={isMutating}
          data-testid="author-restart-ai-button"
        >
          {restartMutation.isPending ? (
            <>
              <Icons.loading className="mr-1.5 size-4 animate-spin" />
              Запускаем…
            </>
          ) : (
            <>
              <Icons.refresh className="mr-1.5 size-4" />
              Перезапустить
            </>
          )}
        </Button>
      </div>
    </div>
  );
}
