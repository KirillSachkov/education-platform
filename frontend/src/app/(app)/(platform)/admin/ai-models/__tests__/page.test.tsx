import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { adminNav } from "@/shared/config/app-navigation";
import { routes } from "@/shared/config/routes";
import AdminAiModelsRoute from "../page";

const { get, put } = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn() }));

vi.mock("@/shared/api", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/api")>();
  return { ...actual, apiClient: { ...actual.apiClient, get, put } };
});

const settings = {
  reviewer: {
    model: "openai/gpt-4.1-mini",
    temperature: 0.2,
    maxOutputTokens: 2000,
    timeoutSeconds: 60,
    source: "CONFIG",
  },
  reviewerBasePrompt: { value: "Review the student changes", source: "CONFIG" },
  reviewEnabled: true,
  repoContextEnabled: false,
  updatedAt: null,
  updatedByUserId: null,
};

function showRoute() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AdminAiModelsRoute />
    </QueryClientProvider>,
  );
}

describe("retained AssignmentReview AI settings route", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    get.mockResolvedValue({ data: { result: settings } });
    put.mockResolvedValue({ data: { result: null } });
  });

  it("keeps the existing model read and save flow reachable through admin navigation", async () => {
    showRoute();
    const model = await screen.findByDisplayValue("openai/gpt-4.1-mini");
    expect(adminNav.some((item) => item.href === routes.adminAiModels)).toBe(true);
    expect(screen.getByRole("heading", { name: "AI-модели для проверки заданий" })).toBeVisible();
    fireEvent.change(model, { target: { value: "synthetic/reviewer" } });
    fireEvent.click(screen.getByRole("button", { name: "Сохранить", exact: true }));
    await waitFor(() =>
      expect(put).toHaveBeenCalledWith("/assignment-review/admin/ai-settings/", {
        reviewer: {
          model: "synthetic/reviewer",
          temperature: 0.2,
          maxOutputTokens: 2000,
          timeoutSeconds: 60,
        },
        reviewerBasePrompt: "Review the student changes",
        reviewEnabled: true,
        repoContextEnabled: false,
      }),
    );
    expect(get.mock.calls.every(([url]) => url === "/assignment-review/admin/ai-settings/")).toBe(
      true,
    );
  });

  it("shows loading without presenting empty editable settings", () => {
    get.mockReturnValue(new Promise(() => {}));
    showRoute();
    expect(screen.getByText("Загрузка настроек…")).toBeVisible();
    expect(screen.queryByPlaceholderText("model id")).not.toBeInTheDocument();
  });

  it("keeps failed reads unavailable for editing", async () => {
    get.mockRejectedValue(new Error("Synthetic read failure"));
    showRoute();
    expect(await screen.findByText("Не удалось загрузить настройки AI ревью.")).toBeVisible();
    expect(screen.queryByPlaceholderText("model id")).not.toBeInTheDocument();
    expect(put).not.toHaveBeenCalled();
  });
});
