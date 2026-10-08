import { apiClient } from "@/shared/api";
import { AUTH_ORIGIN } from "@/shared/config";
import { routes } from "@/shared/config/routes";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ConnectionsView } from "../connections-view";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));

const linkTelegram = vi.fn();
vi.mock("@/features/telegram-link", () => ({
  useTelegramLink: () => ({ linkTelegram, isPending: false }),
}));

// Мокаем только сетевую границу — реальные profileQueryOptions/select работают.
function profileEnvelope(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    result: {
      id: "user-1",
      name: "test",
      displayName: null,
      username: "test",
      email: "test@example.com",
      roles: [],
      bio: null,
      profiles: null,
      hasPassword: false,
      hasGitHubLinked: false,
      hasTelegramLinked: false,
      avatarId: null,
      githubOrgs: [],
      ...overrides,
    },
    isError: false,
    error: null,
    timeGenerated: "2026-07-05T00:00:00Z",
  };
}

vi.mock("@/shared/api", async (orig) => {
  const actual = await orig<Record<string, unknown>>();
  return {
    ...actual,
    apiClient: {
      get: vi.fn(),
    },
  };
});

function renderView(next: string | null) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <ConnectionsView next={next} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.get).mockResolvedValue({ data: profileEnvelope() });
});

describe("ConnectionsView — /onboarding/connections", () => {
  it("renders title, subtitle, both cards and the skip button (copy §6, verbatim)", () => {
    renderView("/home");

    expect(screen.getByRole("heading", { name: "Свяжите аккаунты" })).toBeInTheDocument();
    expect(screen.getByText("Это можно сделать позже в настройках")).toBeInTheDocument();

    expect(
      screen.getByText(
        "Автоматический доступ к курсам по вашей организации и AI-проверка PR в заданиях.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("Чаты курсов, уведомления и помощь.")).toBeInTheDocument();

    expect(screen.getByRole("link", { name: /Привязать GitHub/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Привязать Telegram/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Пропустить/ })).toBeInTheDocument();
  });

  it("GitHub card links to the same auth-service link endpoint as settings", () => {
    renderView("/home");

    expect(screen.getByRole("link", { name: /Привязать GitHub/ })).toHaveAttribute(
      "href",
      `${AUTH_ORIGIN}/auth/github/link`,
    );
  });

  it("Telegram card triggers the telegram-link flow", () => {
    renderView("/home");

    fireEvent.click(screen.getByRole("button", { name: /Привязать Telegram/ }));

    expect(linkTelegram).toHaveBeenCalledTimes(1);
  });

  it("skip navigates to the validated internal next path", () => {
    renderView("/courses/net-fullstack");

    fireEvent.click(screen.getByRole("button", { name: /Пропустить/ }));

    expect(push).toHaveBeenCalledWith("/courses/net-fullstack");
  });

  it("preserves the exact pricing checkout callback in next", () => {
    const next =
      "/pricing?plan=dotnet-fullstack&intent=0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61&resume=checkout";
    renderView(next);

    fireEvent.click(screen.getByRole("button", { name: /Пропустить/ }));

    expect(push).toHaveBeenCalledWith(next);
  });

  it.each(["https://evil.com", "//evil.com", "javascript:alert(1)"])(
    "skip falls back to home for open-redirect next %s",
    (next) => {
      renderView(next);

      fireEvent.click(screen.getByRole("button", { name: /Пропустить/ }));

      expect(push).toHaveBeenCalledWith(routes.home);
    },
  );

  it("skip falls back to home when next is missing", () => {
    renderView(null);

    fireEvent.click(screen.getByRole("button", { name: /Пропустить/ }));

    expect(push).toHaveBeenCalledWith(routes.home);
  });

  it("shows linked states instead of link actions when both accounts are linked", async () => {
    vi.mocked(apiClient.get).mockResolvedValue({
      data: profileEnvelope({ hasGitHubLinked: true, hasTelegramLinked: true }),
    });

    renderView("/home");

    expect(await screen.findByText("GitHub привязан")).toBeInTheDocument();
    expect(screen.getByText("Telegram привязан")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Привязать GitHub/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Привязать Telegram/ })).not.toBeInTheDocument();
  });
});
