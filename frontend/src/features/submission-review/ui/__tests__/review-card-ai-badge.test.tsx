import { describe, it, expect, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { ReviewCard } from "../review-card";
import { ReviewList } from "../review-list";
import type { ReviewTab } from "../review-tabs";
import type { ReviewSubmissionItemDto } from "@/entities/review-submission";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

function makeSubmission(overrides: Partial<ReviewSubmissionItemDto> = {}): ReviewSubmissionItemDto {
  return {
    submissionId: "s1",
    courseId: "c1",
    projectId: "p1",
    issueId: "i1",
    studentId: "u1",
    submissionNo: 1,
    attemptsCount: 1,
    payload: "https://github.com/test/repo/pull/1",
    issueProgressStatus: "UNDER_REVIEW",
    reviewStatus: "PENDING",
    reviewerId: null,
    submittedAt: "2026-05-10T10:00:00Z",
    reviewStartedAt: null,
    reviewedAt: null,
    feedback: null,
    studentName: "Alice",
    studentUsername: "alice",
    studentAvatarId: null,
    studentEmail: null,
    studentTelegramUsername: null,
    reviewerName: null,
    reviewerUsername: null,
    reviewerAvatarId: null,
    courseTitle: "Test course",
    projectTitle: "Test project",
    issueTitle: "Test issue",
    latestAiVerdict: null,
    aiIterationsCount: 0,
    lastAiIterationAt: null,
    aiReviewStatus: null,
    authorHelpRequestedAt: null,
    authorHelpMessage: null,
    studentQuestionAt: null,
    ...overrides,
  };
}

function renderCard(
  submission: ReviewSubmissionItemDto,
  overrides: { onCancelReview?: () => Promise<void> } = {},
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <ReviewCard
        submission={submission}
        onStartReview={vi.fn()}
        onApprove={vi.fn()}
        onRequestChanges={vi.fn()}
        onReopen={vi.fn()}
        onCancelReview={overrides.onCancelReview ?? vi.fn()}
        onMarkComplete={vi.fn()}
        onOpenStatusOverride={vi.fn()}
        isStartPending={false}
        isApprovePending={false}
        isRequestChangesPending={false}
        isReopenPending={false}
        isCancelReviewPending={false}
        isMarkCompletePending={false}
        isSetStatusPending={false}
      />
    </QueryClientProvider>,
  );
}

function renderList(items: ReviewSubmissionItemDto[], activeTab: ReviewTab = "pending") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <ReviewList
        items={items}
        isLoading={false}
        activeTab={activeTab}
        page={1}
        totalPages={1}
        onPageChange={vi.fn()}
        onStartReview={vi.fn()}
        onApprove={vi.fn()}
        onRequestChanges={vi.fn()}
        onReopen={vi.fn()}
        onCancelReview={vi.fn()}
        onMarkComplete={vi.fn()}
        onOpenStatusOverride={vi.fn()}
        isStartPending={false}
        isApprovePending={false}
        isRequestChangesPending={false}
        isReopenPending={false}
        isCancelReviewPending={false}
        isMarkCompletePending={false}
        isSetStatusPending={false}
      />
    </QueryClientProvider>,
  );
}

describe("ReviewCard AI verdict badge", () => {
  it("hides badge when no AI verdict", () => {
    renderCard(makeSubmission({ latestAiVerdict: null }));
    expect(screen.queryByTestId("ai-verdict-badge")).toBeNull();
  });

  it("renders LOOKS_GOOD badge", () => {
    renderCard(makeSubmission({ latestAiVerdict: "LOOKS_GOOD", aiIterationsCount: 1 }));
    const badge = screen.getByTestId("ai-verdict-badge");
    expect(badge).toHaveTextContent("AI: принято");
  });

  it("renders MAJOR_ISSUES badge", () => {
    renderCard(makeSubmission({ latestAiVerdict: "MAJOR_ISSUES", aiIterationsCount: 2 }));
    const badge = screen.getByTestId("ai-verdict-badge");
    expect(badge).toHaveTextContent("AI: серьёзные замечания");
  });

  it("renders OFF_TOPIC badge", () => {
    renderCard(makeSubmission({ latestAiVerdict: "OFF_TOPIC", aiIterationsCount: 1 }));
    const badge = screen.getByTestId("ai-verdict-badge");
    expect(badge).toHaveTextContent("AI: не по теме");
  });

  // #718 — «AI-проверка не запущена» чип закрывает дыру: раньше при обоих null рендерился null.
  it("renders the «not started» chip when both verdict and status are null (#718)", () => {
    renderCard(makeSubmission({ latestAiVerdict: null, aiReviewStatus: null }));
    const chip = screen.getByTestId("ai-notstarted-badge");
    expect(chip).toHaveTextContent("AI-проверка не запущена");
    expect(screen.queryByTestId("ai-verdict-badge")).toBeNull();
    expect(screen.queryByTestId("ai-status-badge")).toBeNull();
  });

  it.each([
    ["QUEUED", "AI в очереди"],
    ["RUNNING", "AI проверяет"],
    ["READY", "AI: проверено"],
    ["FAILED", "AI: ошибка"],
  ] as const)(
    "keeps the %s status chip and hides the «not started» chip (#718)",
    (status, label) => {
      renderCard(makeSubmission({ aiReviewStatus: status, latestAiVerdict: null }));
      expect(screen.getByTestId("ai-status-badge")).toHaveTextContent(label);
      expect(screen.queryByTestId("ai-notstarted-badge")).toBeNull();
    },
  );

  it("hides the «not started» chip when a verdict exists (#718)", () => {
    renderCard(makeSubmission({ latestAiVerdict: "LOOKS_GOOD", aiIterationsCount: 1 }));
    expect(screen.getByTestId("ai-verdict-badge")).toBeInTheDocument();
    expect(screen.queryByTestId("ai-notstarted-badge")).toBeNull();
  });

  it("shows the attempts-count strip only when the card groups multiple attempts (#369)", () => {
    const single = renderCard(makeSubmission({ attemptsCount: 1 }));
    expect(screen.queryByText(/Попыток:/)).toBeNull();
    single.unmount();

    renderCard(makeSubmission({ submissionNo: 3, attemptsCount: 3 }));
    expect(screen.getByText(/Попыток:/)).toBeTruthy();
  });

  it("hides the author-help badge when not requested (#383)", () => {
    renderCard(makeSubmission({ authorHelpRequestedAt: null }));
    expect(screen.queryByTestId("author-help-badge")).toBeNull();
  });

  it("renders the author-help badge when the student summoned the author (#383)", () => {
    renderCard(makeSubmission({ authorHelpRequestedAt: "2026-05-30T10:00:00Z" }));
    const badge = screen.getByTestId("author-help-badge");
    expect(badge).toHaveTextContent("Нужна помощь автора");
  });

  it("hides the student-question badge when no question was asked (#713)", () => {
    renderCard(makeSubmission({ studentQuestionAt: null }));
    expect(screen.queryByTestId("student-question-badge")).toBeNull();
  });

  it("hides the student-question badge when the field is absent (#713)", () => {
    renderCard(makeSubmission());
    expect(screen.queryByTestId("student-question-badge")).toBeNull();
  });

  it("renders the student-question badge when the student asked in the PR (#713)", () => {
    renderCard(makeSubmission({ studentQuestionAt: "2026-07-05T10:00:00Z" }));
    const badge = screen.getByTestId("student-question-badge");
    expect(badge).toHaveTextContent("Новый вопрос от студента");
  });

  it("shows AI history control for a GitHub PR even before AI review exists", () => {
    renderCard(
      makeSubmission({
        aiIterationsCount: 0,
        aiReviewStatus: null,
        latestAiVerdict: null,
      }),
    );

    expect(screen.getByTestId("ai-history-toggle")).toHaveTextContent("AI-история ревью");
  });

  it("does not show AI history control for a non-GitHub submission payload", () => {
    renderCard(makeSubmission({ payload: "https://example.com/manual-review" }));

    expect(screen.queryByTestId("ai-history-toggle")).toBeNull();
  });

  it("does not show AI history control for a GitHub URL that is not a pull request", () => {
    renderCard(makeSubmission({ payload: "https://github.com/test/repo" }));

    expect(screen.queryByTestId("ai-history-toggle")).toBeNull();
  });

  // #668 — PR URL с хвостом (sub-path / #fragment / ?query) теперь распознаётся,
  // AI-блок рендерится (раньше эти URL прятали контролы → ученик завис без AI).
  it.each([
    ["sub-path", "https://github.com/test/repo/pull/1/files"],
    ["fragment", "https://github.com/test/repo/pull/1#pullrequestreview-4580609186"],
    ["query", "https://github.com/test/repo/pull/1?diff=split"],
    ["changes sub-path", "https://github.com/test/repo/pull/6/changes/426891aae3"],
  ])("shows AI history control for a PR URL with a %s tail (#668)", (_label, payload) => {
    renderCard(makeSubmission({ payload }));

    expect(screen.getByTestId("ai-history-toggle")).toHaveTextContent("AI-история ревью");
  });

  it("keeps manual actions visible in the in-review tab", () => {
    renderList(
      [makeSubmission({ reviewStatus: "PENDING", aiReviewStatus: "RUNNING" })],
      "in_review",
    );

    expect(screen.getByRole("button", { name: /Начать ревью/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Отметить выполненным/ })).toBeInTheDocument();
  });

  it("keeps verdict actions visible for an IN_REVIEW card in the in-review tab", () => {
    renderList(
      [
        makeSubmission({
          reviewStatus: "IN_REVIEW",
          aiReviewStatus: "RUNNING",
          reviewerId: "reviewer-1",
        }),
      ],
      "in_review",
    );

    expect(screen.getByRole("button", { name: /Принять/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Доработать/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Отметить выполненным/ })).toBeInTheDocument();
  });

  // #668 — «Отказаться от проверки» (IN_REVIEW → «Ожидает проверки»).
  it("hides «Отказаться от проверки» for a PENDING submission", () => {
    renderCard(makeSubmission({ reviewStatus: "PENDING" }));
    expect(screen.queryByRole("button", { name: /Отказаться от проверки/ })).toBeNull();
  });

  it("shows «Отказаться от проверки» for an IN_REVIEW submission", () => {
    renderCard(makeSubmission({ reviewStatus: "IN_REVIEW", reviewerId: "reviewer-1" }));
    expect(screen.getByRole("button", { name: /Отказаться от проверки/ })).toBeInTheDocument();
  });

  it("calls onCancelReview with the submission when «Отказаться от проверки» is clicked", () => {
    const onCancelReview = vi.fn().mockResolvedValue(undefined);
    const submission = makeSubmission({ reviewStatus: "IN_REVIEW", reviewerId: "reviewer-1" });
    renderCard(submission, { onCancelReview });

    fireEvent.click(screen.getByRole("button", { name: /Отказаться от проверки/ }));

    expect(onCancelReview).toHaveBeenCalledWith(submission);
  });
});
