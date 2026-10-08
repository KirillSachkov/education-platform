"use client";

import type { ReviewSubmissionItemDto } from "@/entities/review-submission";
import { Button } from "@/shared/ui/kit/button";
import { GitPullRequest, Loader2, ChevronLeft, ChevronRight } from "lucide-react";
import { ReviewCard } from "./review-card";
import type { ReviewTab } from "./review-tabs";

/**
 * Stack every attempt of the same `(issueId, studentId)` into ONE card (#383).
 *
 * The backend review-list queries already collapse `issue_submissions` to one
 * row per `issue_progress` (= student + issue) via a `ROW_NUMBER() … WHERE rn=1`
 * CTE and return `attemptsCount` (#369), so in practice each group arrives once.
 * This is a defensive, idempotent client-side guard: if two rows ever share a
 * group, keep the latest attempt (max `submissionNo`) — the one whose status the
 * tab keys off — and fold the rest into its AttemptHistory drill-in. No-op when
 * the list is already grouped. Returns a new array (React Compiler: never mutate
 * in place). Order is preserved from the first time a group is seen, so keyset
 * pagination ordering is untouched.
 */
export function groupBySubmitter(items: ReviewSubmissionItemDto[]): ReviewSubmissionItemDto[] {
  const byGroup = new Map<string, ReviewSubmissionItemDto>();
  const order: string[] = [];

  for (const item of items) {
    const key = `${item.issueId}:${item.studentId}`;
    const existing = byGroup.get(key);
    if (!existing) {
      byGroup.set(key, item);
      order.push(key);
    } else if (item.submissionNo > existing.submissionNo) {
      byGroup.set(key, item);
    }
  }

  return order.map((key) => byGroup.get(key)!);
}

interface ReviewListProps {
  items: ReviewSubmissionItemDto[];
  isLoading: boolean;
  activeTab: ReviewTab;
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  onStartReview: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onApprove: (submission: ReviewSubmissionItemDto) => void;
  onRequestChanges: (submission: ReviewSubmissionItemDto) => void;
  onReopen: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onCancelReview: (submission: ReviewSubmissionItemDto) => Promise<void>;
  onMarkComplete: (submission: ReviewSubmissionItemDto) => void;
  onOpenStatusOverride: (submission: ReviewSubmissionItemDto) => void;
  isStartPending: boolean;
  isApprovePending: boolean;
  isRequestChangesPending: boolean;
  isReopenPending: boolean;
  isCancelReviewPending: boolean;
  isMarkCompletePending: boolean;
  isSetStatusPending: boolean;
}

export function ReviewList({
  items,
  isLoading,
  activeTab,
  page,
  totalPages,
  onPageChange,
  onStartReview,
  onApprove,
  onRequestChanges,
  onReopen,
  onCancelReview,
  onMarkComplete,
  onOpenStatusOverride,
  isStartPending,
  isApprovePending,
  isRequestChangesPending,
  isReopenPending,
  isCancelReviewPending,
  isMarkCompletePending,
  isSetStatusPending,
}: ReviewListProps) {
  // One card per (issueId, studentId) group; older attempts live in the card's
  // AttemptHistory drill-in. Backend already groups (#369) — this is idempotent.
  const groups = groupBySubmitter(items);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 size={24} className="animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (groups.length === 0) {
    return (
      <div className="border-2 border-dashed rounded-2xl p-10 flex flex-col items-center justify-center text-center">
        <div className="size-12 rounded-xl bg-muted flex items-center justify-center mb-3">
          <GitPullRequest size={22} className="text-muted-foreground" />
        </div>
        <p className="text-sm font-medium mb-1">Нет работ</p>
        <p className="text-sm text-muted-foreground">
          {activeTab === "pending"
            ? "Работы, ожидающие проверки, не найдены"
            : activeTab === "in_review"
              ? "Сейчас AI ничего не проверяет"
              : "Проверенных работ пока нет"}
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="space-y-3">
        {groups.map((submission) => (
          <ReviewCard
            key={`${submission.issueId}:${submission.studentId}`}
            submission={submission}
            onStartReview={onStartReview}
            onApprove={onApprove}
            onRequestChanges={onRequestChanges}
            onReopen={onReopen}
            onCancelReview={onCancelReview}
            onMarkComplete={onMarkComplete}
            onOpenStatusOverride={onOpenStatusOverride}
            isStartPending={isStartPending}
            isApprovePending={isApprovePending}
            isRequestChangesPending={isRequestChangesPending}
            isReopenPending={isReopenPending}
            isCancelReviewPending={isCancelReviewPending}
            isMarkCompletePending={isMarkCompletePending}
            isSetStatusPending={isSetStatusPending}
          />
        ))}
      </div>

      {totalPages > 1 && (
        <div className="flex items-center justify-center gap-2 mt-4">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(Math.max(1, page - 1))}
            disabled={page <= 1}
            aria-label="Предыдущая страница"
          >
            <ChevronLeft size={14} />
          </Button>
          <span className="text-sm text-muted-foreground">
            {page} / {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(Math.min(totalPages, page + 1))}
            disabled={page >= totalPages}
            aria-label="Следующая страница"
          >
            <ChevronRight size={14} />
          </Button>
        </div>
      )}
    </>
  );
}
