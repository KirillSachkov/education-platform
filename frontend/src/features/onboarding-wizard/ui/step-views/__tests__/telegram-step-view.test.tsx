import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { apiClient, EnvelopeError } from "@/shared/api";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { TelegramStepView } from "../telegram-step-view";

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// Mock the network boundary only — real query options / select / EnvelopeError run.
// getStatus → linked user with one supergroup chat; recheck → not_member.
vi.mock("@/shared/api", async (orig) => {
  const actual = await orig<Record<string, unknown>>();
  const statusEnvelope = {
    result: {
      isLinked: true,
      telegramUsername: "levanart",
      chats: [
        {
          chatId: "100",
          title: "DotNet Fullstack Course",
          joinUrl: "https://t.me/+abc123",
          chatType: "supergroup",
          enrollmentGrantsMembership: true,
        },
      ],
    },
    isError: false,
    error: null,
    timeGenerated: "2026-06-06T00:00:00Z",
  };
  return {
    ...actual,
    apiClient: {
      get: vi.fn(async () => ({ data: statusEnvelope })),
      post: vi.fn(async () => ({
        data: {
          result: { completed: false, status: "not_member" },
          isError: false,
          error: null,
          timeGenerated: "2026-06-06T00:00:00Z",
        },
      })),
    },
  };
});

function renderStep(completeError?: unknown) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <TelegramStepView
        planId="plan-1"
        onLinkTelegram={vi.fn()}
        isLinking={false}
        completeError={completeError}
      />
    </QueryClientProvider>,
  );
}

const membershipError = new EnvelopeError({
  type: "VALIDATION",
  messages: [
    {
      code: "onboarding.telegram.membership.required",
      message: "Сначала вступите в Telegram-группу — членство не подтверждено.",
    },
  ],
});

describe("TelegramStepView — «Далее» больше не молчит", () => {
  it("renders a human-readable chat-type label instead of the raw Telegram type", async () => {
    renderStep();
    expect(await screen.findByText("DotNet Fullstack Course")).toBeInTheDocument();
    // «supergroup» → «Группа»
    expect(screen.getByText("Группа")).toBeInTheDocument();
    expect(screen.queryByText("supergroup")).not.toBeInTheDocument();
  });

  it("shows an inline membership alert + support link when «Далее» was rejected (mandatory check)", async () => {
    renderStep(membershipError);
    // Inline-обратная связь рендерится даже без видимого тоста — «Далее» не молчит.
    expect(await screen.findByText("Пока не видим тебя в группе")).toBeInTheDocument();
    const supportLink = screen.getByRole("link", { name: /Написать в поддержку/ });
    expect(supportLink).toHaveAttribute("href", PRIMARY_AUTHOR_CONSULTATION_LINK);
  });

  it("does not show the membership alert when there is no completion error", async () => {
    renderStep();
    // Ждём, пока статус-запрос отрисует карточку чата, потом проверяем отсутствие алёрта.
    await screen.findByText("DotNet Fullstack Course");
    expect(screen.queryByText("Пока не видим тебя в группе")).not.toBeInTheDocument();
  });

  it("shows the soft «couldn't verify» copy when «Я вступил — проверить» returns unknown", async () => {
    vi.mocked(apiClient.post).mockResolvedValueOnce({
      data: {
        result: { completed: false, status: "unknown" },
        isError: false,
        error: null,
        timeGenerated: "2026-06-06T00:00:00Z",
      },
    });
    renderStep();
    const recheckBtn = await screen.findByRole("button", { name: /Я вступил — проверить/ });
    fireEvent.click(recheckBtn);
    expect(await screen.findByText(/Не удалось проверить членство/)).toBeInTheDocument();
  });
});
