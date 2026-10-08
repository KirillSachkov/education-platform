import { describe, it, expect, vi, afterEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StudentQuestionThread } from "../student-question-thread";
import { apiClient } from "@/shared/api";
import type { StudentPrMessageDto } from "@/entities/ai-review";

function makeMessage(overrides: Partial<StudentPrMessageDto> = {}): StudentPrMessageDto {
  return {
    id: "m1",
    githubCommentId: 111,
    inReplyToGithubId: null,
    authorGithubLogin: "alice",
    body: "Почему тут MAJOR? Я же вынес логику в сервис.",
    path: null,
    line: null,
    commentUrl: "https://github.com/test/repo/pull/1#discussion_r111",
    createdAt: "2026-07-05T10:00:00Z",
    answeredAt: null,
    answerBody: null,
    ...overrides,
  };
}

function renderThread(messages: StudentPrMessageDto[], submissionId = "s1") {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      <StudentQuestionThread submissionId={submissionId} messages={messages} />
    </QueryClientProvider>,
  );
}

describe("StudentQuestionThread", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders nothing when there are no messages (#713)", () => {
    const { container } = renderThread([]);
    expect(screen.queryByTestId("student-question-thread")).toBeNull();
    expect(container).toBeEmptyDOMElement();
  });

  it("renders a student message with author, body, path:line and PR link", () => {
    renderThread([
      makeMessage({
        authorGithubLogin: "bob",
        body: "Не понял замечание по null-проверке",
        path: "src/Service.cs",
        line: 42,
      }),
    ]);

    expect(screen.getByTestId("student-question-thread")).toBeInTheDocument();
    const item = screen.getByTestId("student-question-item");
    expect(item).toHaveTextContent("@bob");
    expect(item).toHaveTextContent("Не понял замечание по null-проверке");
    expect(item).toHaveTextContent("src/Service.cs:42");
    expect(screen.getByTestId("student-question-pr-link")).toHaveAttribute(
      "href",
      "https://github.com/test/repo/pull/1#discussion_r111",
    );
  });

  it("shows a reply form for an unanswered message and no answer block", () => {
    renderThread([makeMessage({ answeredAt: null })]);
    expect(screen.getByTestId("student-reply-input")).toBeInTheDocument();
    expect(screen.getByTestId("student-reply-submit")).toBeInTheDocument();
    expect(screen.queryByTestId("student-answer")).toBeNull();
  });

  it("shows the author answer and hides the reply form for an answered message", () => {
    renderThread([
      makeMessage({
        answeredAt: "2026-07-05T12:00:00Z",
        answerBody: "Замечание про сервис снял, но остаётся вопрос по валидации.",
      }),
    ]);

    const answer = screen.getByTestId("student-answer");
    expect(answer).toHaveTextContent("Отвечено");
    expect(answer).toHaveTextContent("остаётся вопрос по валидации");
    expect(screen.queryByTestId("student-reply-input")).toBeNull();
  });

  it("disables the reply button while the textarea is empty", () => {
    renderThread([makeMessage()]);
    expect(screen.getByTestId("student-reply-submit")).toBeDisabled();
  });

  it("posts the reply to the correct URL with a trimmed body when submitted", async () => {
    const postSpy = vi.spyOn(apiClient, "post").mockResolvedValue({
      data: {
        isError: false,
        result: {
          messageId: "m1",
          answerGithubCommentId: 222,
          answerHtmlUrl: "https://github.com/test/repo/pull/1#discussion_r222",
          answeredAt: "2026-07-05T12:00:00Z",
        },
      },
    });

    renderThread([makeMessage({ id: "msg-42" })]);

    fireEvent.change(screen.getByTestId("student-reply-input"), {
      target: { value: "  Согласен, поправил verdict вручную.  " },
    });

    const submit = screen.getByTestId("student-reply-submit");
    expect(submit).not.toBeDisabled();
    fireEvent.click(submit);

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith("/assignment-review/student-messages/msg-42/reply/", {
        body: "Согласен, поправил verdict вручную.",
      });
    });
  });

  it("does not post when the textarea holds only whitespace", () => {
    const postSpy = vi.spyOn(apiClient, "post");
    renderThread([makeMessage()]);

    fireEvent.change(screen.getByTestId("student-reply-input"), {
      target: { value: "   " },
    });

    expect(screen.getByTestId("student-reply-submit")).toBeDisabled();
    fireEvent.click(screen.getByTestId("student-reply-submit"));
    expect(postSpy).not.toHaveBeenCalled();
  });
});
