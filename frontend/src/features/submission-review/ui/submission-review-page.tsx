"use client";

import { useState } from "react";
import type { ReviewSubmissionItemDto } from "@/entities/review-submission";
import { reviewSubmissionsQueryOptions } from "@/entities/review-submission";
import { useApproveReview } from "../model/use-approve-review";
import { useMarkCompleteReview } from "../model/use-mark-complete-review";
import { useCancelReview } from "../model/use-cancel-review";
import { useReopenReview } from "../model/use-reopen-review";
import { useRequestReviewChanges } from "../model/use-request-review-changes";
import { useSetReviewIssueStatus } from "../model/use-set-review-issue-status";
import { useStartReview } from "../model/use-start-review";
import type { IssueProgressStatus } from "@/shared/types/status";
import { useQuery } from "@tanstack/react-query";
import { ReviewList } from "./review-list";
import { FeedbackDialog, type FeedbackDialogIntent } from "./feedback-dialog";
import { ReviewTabs, type ReviewTab } from "./review-tabs";
import { StatusOverrideDialog } from "./status-override-dialog";

interface ActiveFeedback {
  intent: FeedbackDialogIntent;
  submission: ReviewSubmissionItemDto;
}

interface ActiveStatusOverride {
  submission: ReviewSubmissionItemDto;
}

export function SubmissionReviewPage({ focusedSubmissionId }: { focusedSubmissionId?: string }) {
  const [activeTab, setActiveTab] = useState<ReviewTab>("pending");
  const [page, setPage] = useState(1);
  const [dismissedFocusId, setDismissedFocusId] = useState<string>();
  const [activeFeedback, setActiveFeedback] = useState<ActiveFeedback | null>(null);
  const [activeStatusOverride, setActiveStatusOverride] = useState<ActiveStatusOverride | null>(
    null,
  );

  const focusMode = focusedSubmissionId !== undefined && dismissedFocusId !== focusedSubmissionId;
  const visiblePage = focusMode ? 1 : page;
  const filters = {
    page: visiblePage,
    pageSize: 20,
    ...(focusMode ? { submissionId: focusedSubmissionId } : {}),
  };

  const { data: pendingData, isLoading: isPendingLoading } = useQuery({
    ...reviewSubmissionsQueryOptions.pendingList(filters),
    enabled: focusMode || activeTab === "pending",
  });

  const { data: inReviewData, isLoading: isInReviewLoading } = useQuery({
    ...reviewSubmissionsQueryOptions.inReviewList(filters),
    enabled: focusMode || activeTab === "in_review",
  });

  const { data: reviewedData, isLoading: isReviewedLoading } = useQuery({
    ...reviewSubmissionsQueryOptions.reviewedList(filters),
    enabled: focusMode || activeTab === "reviewed",
  });

  const { startReview, isPending: isStartPending } = useStartReview();
  const { approveReview, isPending: isApprovePending } = useApproveReview();
  const { requestReviewChanges, isPending: isRequestChangesPending } = useRequestReviewChanges();
  const { reopenReview, isPending: isReopenPending } = useReopenReview();
  const { cancelReview, isPending: isCancelReviewPending } = useCancelReview();
  const { markCompleteReview, isPending: isMarkCompletePending } = useMarkCompleteReview();
  const { setReviewIssueStatus, isPending: isSetStatusPending } = useSetReviewIssueStatus();

  const focusedTab: ReviewTab | null = focusMode
    ? pendingData?.items.length
      ? "pending"
      : inReviewData?.items.length
        ? "in_review"
        : reviewedData?.items.length
          ? "reviewed"
          : null
    : null;
  const visibleTab = focusedTab ?? activeTab;
  const activeData =
    visibleTab === "pending"
      ? pendingData
      : visibleTab === "in_review"
        ? inReviewData
        : reviewedData;
  const activeItems = activeData?.items ?? [];
  const isLoading =
    focusMode && focusedTab === null
      ? isPendingLoading || isInReviewLoading || isReviewedLoading
      : visibleTab === "pending"
        ? isPendingLoading
        : visibleTab === "in_review"
          ? isInReviewLoading
          : isReviewedLoading;
  const totalPages = activeData?.totalPages ?? 0;

  const handleTabChange = (tab: ReviewTab) => {
    setDismissedFocusId(focusedSubmissionId);
    setActiveTab(tab);
    setPage(1);
  };

  const handleStartReview = async (submission: ReviewSubmissionItemDto) => {
    await startReview({
      courseId: submission.courseId,
      submissionId: submission.submissionId,
    });
  };

  const handleOpenApprove = (submission: ReviewSubmissionItemDto) => {
    setActiveFeedback({ intent: "approve", submission });
  };

  const handleOpenRequestChanges = (submission: ReviewSubmissionItemDto) => {
    setActiveFeedback({ intent: "requestChanges", submission });
  };

  const handleReopen = async (submission: ReviewSubmissionItemDto) => {
    await reopenReview({
      courseId: submission.courseId,
      submissionId: submission.submissionId,
    });
  };

  const handleCancelReview = async (submission: ReviewSubmissionItemDto) => {
    await cancelReview({
      courseId: submission.courseId,
      submissionId: submission.submissionId,
    });
  };

  const handleOpenMarkComplete = (submission: ReviewSubmissionItemDto) => {
    setActiveFeedback({ intent: "markComplete", submission });
  };

  const handleOpenStatusOverride = (submission: ReviewSubmissionItemDto) => {
    setActiveStatusOverride({ submission });
  };

  const handleSubmitFeedback = async (feedback: string | null) => {
    if (!activeFeedback) return;

    const { intent, submission } = activeFeedback;
    if (intent === "approve") {
      await approveReview({
        courseId: submission.courseId,
        submissionId: submission.submissionId,
        feedback,
      });
    } else if (intent === "requestChanges") {
      await requestReviewChanges({
        courseId: submission.courseId,
        submissionId: submission.submissionId,
        feedback,
      });
    } else {
      await markCompleteReview({
        courseId: submission.courseId,
        submissionId: submission.submissionId,
        feedback,
      });
    }

    setActiveFeedback(null);
  };

  const handleSubmitStatusOverride = async (
    targetStatus: IssueProgressStatus,
    feedback: string | null,
  ) => {
    if (!activeStatusOverride) return;

    const { submission } = activeStatusOverride;
    await setReviewIssueStatus({
      courseId: submission.courseId,
      issueId: submission.issueId,
      userId: submission.studentId,
      targetStatus,
      feedback,
    });
    setActiveStatusOverride(null);
  };

  const isActiveFeedbackPending =
    activeFeedback?.intent === "approve"
      ? isApprovePending
      : activeFeedback?.intent === "markComplete"
        ? isMarkCompletePending
        : isRequestChangesPending;

  return (
    <div className="max-w-6xl mx-auto p-4 sm:p-6">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold">Проверка работ</h1>
          <p className="text-sm text-muted-foreground">
            Просматривайте и оценивайте работы студентов
          </p>
        </div>
      </div>

      <div className="-mx-4 mb-5 overflow-x-auto px-4 sm:mx-0 sm:px-0 [&::-webkit-scrollbar]:hidden [scrollbar-width:none]">
        <ReviewTabs activeTab={visibleTab} onChange={handleTabChange} />
      </div>

      <ReviewList
        items={activeItems}
        isLoading={isLoading}
        activeTab={visibleTab}
        page={visiblePage}
        totalPages={totalPages}
        onPageChange={setPage}
        onStartReview={handleStartReview}
        onApprove={handleOpenApprove}
        onRequestChanges={handleOpenRequestChanges}
        onReopen={handleReopen}
        onCancelReview={handleCancelReview}
        onMarkComplete={handleOpenMarkComplete}
        onOpenStatusOverride={handleOpenStatusOverride}
        isStartPending={isStartPending}
        isApprovePending={isApprovePending}
        isRequestChangesPending={isRequestChangesPending}
        isReopenPending={isReopenPending}
        isCancelReviewPending={isCancelReviewPending}
        isMarkCompletePending={isMarkCompletePending}
        isSetStatusPending={isSetStatusPending}
      />

      <FeedbackDialog
        open={activeFeedback != null}
        intent={activeFeedback?.intent ?? "requestChanges"}
        onOpenChange={(open) => {
          if (!open) {
            setActiveFeedback(null);
          }
        }}
        onSubmit={handleSubmitFeedback}
        isPending={isActiveFeedbackPending}
        submissionLabel={
          activeFeedback == null
            ? ""
            : (activeFeedback.submission.issueTitle ??
              `${activeFeedback.submission.issueId.slice(0, 8)}...`)
        }
      />

      {activeStatusOverride && (
        <StatusOverrideDialog
          open
          currentStatus={activeStatusOverride.submission.issueProgressStatus}
          submissionLabel={
            activeStatusOverride.submission.issueTitle ??
            `${activeStatusOverride.submission.issueId.slice(0, 8)}...`
          }
          onOpenChange={(open) => {
            if (!open) {
              setActiveStatusOverride(null);
            }
          }}
          onSubmit={handleSubmitStatusOverride}
          isPending={isSetStatusPending}
        />
      )}
    </div>
  );
}
