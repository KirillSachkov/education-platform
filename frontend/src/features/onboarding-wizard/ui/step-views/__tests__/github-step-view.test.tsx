import { afterEach, describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { apiClient, EnvelopeError } from "@/shared/api";
import { PRIMARY_AUTHOR_CONSULTATION_LINK } from "@/shared/config/primary-author";
import { GithubStepView } from "../github-step-view";

const searchParams = vi.hoisted(() => ({ current: new URLSearchParams() }));
vi.mock("next/navigation", () => ({ useSearchParams: () => searchParams.current }));

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}));

// Mock the network boundary only — real query options / select / EnvelopeError run.
vi.mock("@/shared/api", async (orig) => {
  const actual = await orig<Record<string, unknown>>();
  return {
    ...actual,
    apiClient: { get: vi.fn(), post: vi.fn() },
  };
});

function envelope(result: unknown) {
  return {
    data: {
      result,
      isError: false,
      error: null,
      timeGenerated: "2026-06-07T00:00:00Z",
    },
  };
}

/** Routes the two GETs the step makes: profile (/users/me) and invitation status. */
function setupApi(opts: {
  githubOrgs: string[];
  gitHubUrl: string | null;
  invitation: unknown;
  hasGitHubLinked?: boolean;
}) {
  vi.mocked(apiClient.get).mockImplementation(async (url: string) => {
    if (url === "/users/me") {
      return envelope({
        hasGitHubLinked: opts.hasGitHubLinked ?? opts.gitHubUrl !== null,
        githubOrgs: opts.githubOrgs,
        profiles: { student: { gitHubUrl: opts.gitHubUrl } },
      }) as never;
    }
    if (url.includes("/access/integrations/github/invitations/")) {
      return envelope(opts.invitation) as never;
    }
    throw new Error(`unexpected GET ${url}`);
  });
}

function renderStep(completeError?: unknown) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <GithubStepView
        planId="plan-1"
        planGitHubOrgSlug="sachkovtech"
        completeError={completeError}
      />
    </QueryClientProvider>,
  );
}

const membershipError = new EnvelopeError({
  type: "VALIDATION",
  messages: [
    {
      code: "onboarding.github.membership.required",
      message: "Сначала примите приглашение в GitHub-организацию — членство пока не подтверждено.",
    },
  ],
});

describe("GithubStepView — «Далее» не противоречит зелёной карточке", () => {
  it("renders the green «already member» card and no membership alert", async () => {
    setupApi({
      githubOrgs: ["sachkovtech"],
      gitHubUrl: "https://github.com/octocat",
      invitation: null,
    });
    renderStep();
    expect(await screen.findByText(/Вы состоите в org/)).toBeInTheDocument();
    expect(
      screen.queryByText("Членство в GitHub-организации ещё не подтверждено"),
    ).not.toBeInTheDocument();
  });

  it("shows an inline membership alert + support link when «Далее» was rejected (mandatory check)", async () => {
    setupApi({ githubOrgs: [], gitHubUrl: "https://github.com/octocat", invitation: null });
    renderStep(membershipError);
    // Inline-обратная связь рендерится даже без видимого тоста — «Далее» не молчит на мобиле.
    expect(
      await screen.findByText("Членство в GitHub-организации ещё не подтверждено"),
    ).toBeInTheDocument();
    const supportLink = screen.getByRole("link", { name: /Написать в поддержку/ });
    expect(supportLink).toHaveAttribute("href", PRIMARY_AUTHOR_CONSULTATION_LINK);
  });

  it("does not show the membership alert when there is no completion error", async () => {
    setupApi({ githubOrgs: [], gitHubUrl: "https://github.com/octocat", invitation: null });
    renderStep();
    // Ждём, пока отрисуется invite-карточка, потом проверяем отсутствие алёрта.
    await screen.findByText(/Получить приглашение/);
    expect(
      screen.queryByText("Членство в GitHub-организации ещё не подтверждено"),
    ).not.toBeInTheDocument();
  });
});

describe("GithubStepView — состояние привязки (#1148)", () => {
  afterEach(() => {
    searchParams.current = new URLSearchParams();
  });

  it("asks a linked user without a login to refresh the link, not to link again", async () => {
    setupApi({ githubOrgs: [], gitHubUrl: null, hasGitHubLinked: true, invitation: null });
    renderStep();
    expect(await screen.findByText("Обнови привязку GitHub")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Обновить привязку/ })).toBeInTheDocument();
    expect(screen.queryByText("Привяжи GitHub")).not.toBeInTheDocument();
  });

  it("uses hasGitHubLinked as the authority even when gitHubUrl is present", async () => {
    setupApi({
      githubOrgs: [],
      gitHubUrl: "https://github.com/octocat",
      hasGitHubLinked: false,
      invitation: null,
    });
    renderStep();
    expect(await screen.findByText("Привяжи GitHub")).toBeInTheDocument();
    expect(screen.queryByText(/Получить приглашение/)).not.toBeInTheDocument();
  });

  it("shows the OAuth callback conflict inside the step", async () => {
    searchParams.current = new URLSearchParams("account=github-link-conflict");
    setupApi({ githubOrgs: [], gitHubUrl: null, invitation: null });
    renderStep();
    expect(await screen.findByRole("alert")).toHaveTextContent(
      /уже привязан к другому пользователю платформы/,
    );
  });

  it("explains an already-linked callback when the login is still missing", async () => {
    searchParams.current = new URLSearchParams("account=github-already-linked");
    setupApi({ githubOrgs: [], gitHubUrl: null, hasGitHubLinked: true, invitation: null });
    renderStep();
    expect(await screen.findByRole("alert")).toHaveTextContent(/привязан другой GitHub-аккаунт/);
  });

  it("separates «verification unavailable» from «not a member»", async () => {
    setupApi({ githubOrgs: [], gitHubUrl: "https://github.com/octocat", invitation: null });
    renderStep(
      new EnvelopeError({
        type: "VALIDATION",
        messages: [
          {
            code: "onboarding.github.verification.unavailable",
            message: "Не удалось проверить членство",
          },
        ],
      }),
    );
    expect(await screen.findByText("Не удалось проверить членство")).toBeInTheDocument();
    expect(
      screen.queryByText("Членство в GitHub-организации ещё не подтверждено"),
    ).not.toBeInTheDocument();
  });
});
