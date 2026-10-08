import { reviewSubmissionsApi, type ReviewSubmissionItemDto } from "@/entities/review-submission";
import type { Envelope, PaginationResponse } from "@/shared/api";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, type MockInstance, vi } from "vitest";
import { SubmissionReviewPage } from "../submission-review-page";

const FOCUSED_SUBMISSION_ID = "01900000-0000-7000-8000-000000000001";
const SECOND_SUBMISSION_ID = "01900000-0000-7000-8000-000000000002";

type ReviewListResponse = Envelope<PaginationResponse<ReviewSubmissionItemDto>>;
type ReviewListMock = MockInstance<typeof reviewSubmissionsApi.getPending>;

function reviewListSpy(method: "getPending" | "getInReview" | "getReviewed"): ReviewListMock {
  switch (method) {
    case "getPending":
      return vi.spyOn(reviewSubmissionsApi, "getPending");
    case "getInReview":
      return vi.spyOn(reviewSubmissionsApi, "getInReview");
    case "getReviewed":
      return vi.spyOn(reviewSubmissionsApi, "getReviewed");
  }
}

function envelope(
  items: ReviewSubmissionItemDto[],
  pagination: Partial<PaginationResponse<ReviewSubmissionItemDto>> = {},
): ReviewListResponse {
  return {
    result: {
      items,
      totalCount: items.length,
      page: 1,
      pageSize: 20,
      totalPages: items.length > 0 ? 1 : 0,
      ...pagination,
    },
    error: null,
    isError: false,
    timeGenerated: "2026-08-19T10:00:00Z",
  };
}

function submission(overrides: Partial<ReviewSubmissionItemDto> = {}): ReviewSubmissionItemDto {
  return {
    submissionId: FOCUSED_SUBMISSION_ID,
    courseId: "019ffd53-7523-73fe-b9c3-fd0d19217cf3",
    projectId: "019ffd53-7523-73fe-b9c3-fd0d19217cf4",
    issueId: "019ffd53-7523-73fe-b9c3-fd0d19217cf5",
    studentId: "019ffd53-7523-73fe-b9c3-fd0d19217cf6",
    submissionNo: 1,
    attemptsCount: 1,
    payload: "https://example.com/submission",
    issueProgressStatus: "COMPLETED",
    reviewStatus: "APPROVED",
    reviewerId: null,
    submittedAt: "2026-08-15T06:00:00Z",
    reviewStartedAt: "2026-08-15T06:05:00Z",
    reviewedAt: "2026-08-15T06:10:00Z",
    feedback: null,
    studentName: "Target Student",
    studentUsername: "student",
    studentAvatarId: null,
    studentEmail: null,
    studentTelegramUsername: null,
    reviewerName: null,
    reviewerUsername: null,
    reviewerAvatarId: null,
    courseTitle: ".NET Fullstack",
    projectTitle: "Data Structures",
    issueTitle: "Target issue",
    latestAiVerdict: null,
    aiIterationsCount: 0,
    lastAiIterationAt: null,
    aiReviewStatus: null,
    authorHelpRequestedAt: "2026-08-19T01:02:00Z",
    authorHelpMessage: "Please help with this submission",
    studentQuestionAt: null,
    ...overrides,
  };
}

function renderPage(ui: ReactNode) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return render(ui, {
    wrapper: ({ children }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    ),
  });
}

describe("SubmissionReviewPage deep link", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("finds the exact submission across all tabs and opens its current tab", async () => {
    const pending = reviewListSpy("getPending").mockResolvedValue(envelope([]));
    const inReview = reviewListSpy("getInReview").mockResolvedValue(envelope([]));
    const reviewed = reviewListSpy("getReviewed").mockResolvedValue(envelope([submission()]));

    renderPage(<SubmissionReviewPage focusedSubmissionId={FOCUSED_SUBMISSION_ID} />);

    expect(await screen.findByText("Target Student")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Проверенные" })).toHaveClass("bg-background");
    expect(pending).toHaveBeenCalledWith(
      expect.objectContaining({ submissionId: FOCUSED_SUBMISSION_ID }),
      expect.any(Object),
    );
    expect(inReview).toHaveBeenCalledWith(
      expect.objectContaining({ submissionId: FOCUSED_SUBMISSION_ID }),
      expect.any(Object),
    );
    expect(reviewed).toHaveBeenCalledWith(
      expect.objectContaining({ submissionId: FOCUSED_SUBMISSION_ID }),
      expect.any(Object),
    );
  });

  it("keeps the ordinary queue lazy when no submission is focused", async () => {
    reviewListSpy("getPending").mockResolvedValue(
      envelope([submission({ reviewStatus: "PENDING", issueProgressStatus: "UNDER_REVIEW" })]),
    );
    const inReview = reviewListSpy("getInReview").mockResolvedValue(envelope([]));
    const reviewed = reviewListSpy("getReviewed").mockResolvedValue(envelope([]));

    renderPage(<SubmissionReviewPage />);

    expect(await screen.findByText("Target Student")).toBeInTheDocument();
    expect(inReview).not.toHaveBeenCalled();
    expect(reviewed).not.toHaveBeenCalled();
  });

  it("focuses a new notification after the previous focus was dismissed", async () => {
    reviewListSpy("getPending").mockResolvedValue(envelope([]));
    reviewListSpy("getInReview").mockResolvedValue(envelope([]));
    reviewListSpy("getReviewed")
      .mockResolvedValueOnce(envelope([submission({ studentName: "Target A" })]))
      .mockResolvedValue(
        envelope([submission({ submissionId: SECOND_SUBMISSION_ID, studentName: "Target B" })]),
      );

    const { rerender } = renderPage(
      <SubmissionReviewPage focusedSubmissionId={FOCUSED_SUBMISSION_ID} />,
    );

    expect(await screen.findByText("Target A")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Ожидают проверки" }));

    rerender(<SubmissionReviewPage focusedSubmissionId={SECOND_SUBMISSION_ID} />);

    expect(await screen.findByText("Target B")).toBeInTheDocument();
  });

  it("resets pagination while focusing a notification", async () => {
    const pending = reviewListSpy("getPending").mockResolvedValue(
      envelope([submission({ reviewStatus: "PENDING" })], {
        totalCount: 21,
        totalPages: 2,
      }),
    );
    const inReview = reviewListSpy("getInReview").mockResolvedValue(envelope([]));
    const reviewed = reviewListSpy("getReviewed").mockResolvedValue(envelope([]));

    const { rerender } = renderPage(<SubmissionReviewPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Следующая страница" }));
    await waitFor(() => {
      expect(pending).toHaveBeenCalledWith(
        expect.objectContaining({ page: 2 }),
        expect.any(Object),
      );
    });

    rerender(<SubmissionReviewPage focusedSubmissionId={FOCUSED_SUBMISSION_ID} />);

    await waitFor(() => {
      for (const request of [pending, inReview, reviewed]) {
        expect(request).toHaveBeenLastCalledWith(
          expect.objectContaining({ page: 1, submissionId: FOCUSED_SUBMISSION_ID }),
          expect.any(Object),
        );
      }
    });
  });
});
