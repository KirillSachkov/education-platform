import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import {
  QueryClient,
  QueryClientProvider,
  useQuery,
} from "@tanstack/react-query";
import {
  currentOnboardingQueryOptions,
  type CurrentOnboardingResponse,
} from "@/entities/plan-onboarding";
import { routes } from "@/shared/config/routes";
import { useCompleteOnboarding } from "../use-step-actions";

const { pushMock } = vi.hoisted(() => ({ pushMock: vi.fn() }));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: pushMock }),
}));

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// Mock the network boundary only — real `currentOnboardingQueryOptions` +
// `useCompleteOnboarding` run. getCurrent returns null (как реальный бэк после
// complete: GetCurrentOnboardingHandler фильтрует CompletedAt == null).
vi.mock("@/shared/api", async (orig) => {
  const actual = await orig<Record<string, unknown>>();
  return {
    ...actual,
    apiClient: {
      get: vi.fn(async () => ({
        data: { result: null, isError: false, error: null, timeGenerated: "2026-05-31T18:00:00Z" },
      })),
      post: vi.fn(async () => ({
        data: { result: "grant-id", isError: false, error: null, timeGenerated: "2026-05-31T18:00:00Z" },
      })),
    },
  };
});

function makeOnboarding(): CurrentOnboardingResponse {
  return {
    planId: "plan-1",
    planDisplayName: "Полный доступ",
    planAuthorId: "author-1",
    planGitHubOrgSlug: null,
    steps: [],
    state: {
      startedAt: "2026-05-31T18:00:00Z",
      completedAt: null,
      currentStepId: null,
      skippedStepIds: [],
      completedStepIds: [],
    },
  };
}

/**
 * Зеркалит `OnboardingOverlay`: наблюдает `currentOnboardingQueryOptions`
 * (с тем же `placeholderData`) и гейтит рендер на `!data`. ВАЖНО: рендерится
 * sibling'ом к кнопке-мутации, поэтому re-render мутации (pending→success) НЕ
 * перерисовывает этот observer — ровно как в проде, где overlay-родитель не
 * перерисовывается, когда мутация завершается в дочернем wizard.
 */
function OverlayProbe() {
  const { data } = useQuery({
    ...currentOnboardingQueryOptions,
    placeholderData: (prev) => prev,
  });
  return <span data-testid="overlay">{data ? "OPEN" : "CLOSED"}</span>;
}

function FinishProbe() {
  const finish = useCompleteOnboarding("plan-1");
  return (
    <button type="button" data-testid="finish" onClick={() => finish.mutate()}>
      Начать обучение
    </button>
  );
}

describe("useCompleteOnboarding — closes the onboarding overlay", () => {
  beforeEach(() => {
    pushMock.mockClear();
  });

  it("nulls the current-onboarding observer on success without an external re-render", async () => {
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    // Seed как будто overlay уже показывает CompletionView.
    qc.setQueryData(currentOnboardingQueryOptions.queryKey, {
      result: makeOnboarding(),
      isError: false,
      error: null,
      timeGenerated: "2026-05-31T18:00:00Z",
    });

    render(
      <QueryClientProvider client={qc}>
        <OverlayProbe />
        <FinishProbe />
      </QueryClientProvider>,
    );

    expect(screen.getByTestId("overlay")).toHaveTextContent("OPEN");

    await act(async () => {
      fireEvent.click(screen.getByTestId("finish"));
    });

    // Overlay должен закрыться по нотификации кэша — без навигации/постороннего
    // ре-рендера (мы не симулируем смену роута; в проде юзер уже на /home).
    await waitFor(() =>
      expect(screen.getByTestId("overlay")).toHaveTextContent("CLOSED"),
    );

    expect(pushMock).toHaveBeenCalledWith(routes.home);
  });
});
