import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import type * as ReactQuery from "@tanstack/react-query";

import { AskAuthorQuestionButton } from "../ask-author-question-button";

// Управляем состоянием GET «вопрос задан» и мутацией детерминированно, без сети.
let mockState: { askedAt: string | null } | undefined;
const mockMutate = vi.fn();
let mockMutation: { mutate: typeof mockMutate; isPending: boolean; isSuccess: boolean };

vi.mock("@tanstack/react-query", async (importOriginal) => {
  const actual = await importOriginal<typeof ReactQuery>();
  return { ...actual, useQuery: () => ({ data: mockState }) };
});

vi.mock("@/features/ask-author-question", () => ({
  authorQuestionStateQueryOptions: (issueId: string) => ({
    queryKey: ["issue-author-question", issueId],
    queryFn: vi.fn(),
  }),
  useAskAuthorQuestion: () => mockMutation,
}));

describe("AskAuthorQuestionButton (#693)", () => {
  beforeEach(() => {
    mockMutate.mockReset();
    mockState = undefined;
    mockMutation = { mutate: mockMutate, isPending: false, isSuccess: false };
  });

  it("показывает CTA, когда вопрос ещё не задан", () => {
    mockState = { askedAt: null };
    render(<AskAuthorQuestionButton issueId="i1" enabled />);
    expect(screen.getByTestId("ask-author-question-button")).toBeInTheDocument();
    expect(screen.queryByTestId("author-question-asked-state")).not.toBeInTheDocument();
  });

  it("показывает «Вопрос отправлен», когда сервер уже знает о вопросе (askedAt)", () => {
    mockState = { askedAt: "2026-06-29T10:00:00Z" };
    render(<AskAuthorQuestionButton issueId="i1" enabled />);
    expect(screen.getByTestId("author-question-asked-state")).toBeInTheDocument();
    expect(screen.queryByTestId("ask-author-question-button")).not.toBeInTheDocument();
  });

  it("показывает подтверждение сразу после успешной мутации (optimistic-state)", () => {
    mockState = { askedAt: null };
    mockMutation = { mutate: mockMutate, isPending: false, isSuccess: true };
    render(<AskAuthorQuestionButton issueId="i1" enabled />);
    expect(screen.getByTestId("author-question-asked-state")).toBeInTheDocument();
  });

  it("шлёт вопрос обрезанным (trim) при сабмите непустого текста", () => {
    mockState = { askedAt: null };
    render(<AskAuthorQuestionButton issueId="i1" enabled />);
    fireEvent.click(screen.getByTestId("ask-author-question-button"));

    const input = screen.getByTestId("ask-author-question-input");
    fireEvent.change(input, { target: { value: "  не понимаю требование  " } });
    fireEvent.click(screen.getByTestId("ask-author-question-submit"));

    expect(mockMutate).toHaveBeenCalledTimes(1);
    expect(mockMutate.mock.calls[0][0]).toBe("не понимаю требование");
  });

  it("не шлёт вопрос, если текст пустой (submit задизейблен)", () => {
    mockState = { askedAt: null };
    render(<AskAuthorQuestionButton issueId="i1" enabled />);
    fireEvent.click(screen.getByTestId("ask-author-question-button"));

    const submit = screen.getByTestId("ask-author-question-submit");
    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(mockMutate).not.toHaveBeenCalled();
  });
});
