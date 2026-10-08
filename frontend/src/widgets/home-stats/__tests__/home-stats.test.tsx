import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type * as ReactQueryModule from "@tanstack/react-query";
import type { MyActivityDto } from "@/entities/user-activity";

// useQuery подменяем, но сохраняем queryOptions — его зовёт entities/user-activity/api.
vi.mock("@tanstack/react-query", async (importOriginal) => {
  const actual = await importOriginal<typeof ReactQueryModule>();
  return { ...actual, useQuery: vi.fn() };
});
vi.mock("@/shared/auth", () => ({ useIsAuthenticated: vi.fn() }));

import { useQuery } from "@tanstack/react-query";
import { useIsAuthenticated } from "@/shared/auth";
import { HomeStats } from "../home-stats";

const mockUseQuery = vi.mocked(useQuery);
const mockUseAuth = vi.mocked(useIsAuthenticated);

function mockQuery(state: {
  data?: MyActivityDto;
  isLoading?: boolean;
  error?: Error | null;
}) {
  mockUseQuery.mockReturnValue({
    data: state.data,
    isLoading: state.isLoading ?? false,
    error: state.error ?? null,
    refetch: vi.fn(),
  } as unknown as ReturnType<typeof useQuery>);
}

const sampleActivity: MyActivityDto = {
  days: [
    { date: "2026-06-26", xp: 0, materialsCompleted: 0 },
    { date: "2026-06-27", xp: 60, materialsCompleted: 2 },
    { date: "2026-06-28", xp: 10, materialsCompleted: 1 },
  ],
  streak: { current: 7, longest: 12 },
  totals: { totalXp: 680, materialsCompleted: 28, issuesApproved: 3 },
};

describe("HomeStats", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing for anonymous users (access gate)", () => {
    mockUseAuth.mockReturnValue(false);
    mockQuery({ data: undefined, isLoading: false });
    const { container } = render(<HomeStats />);
    expect(container.innerHTML).toBe("");
  });

  it("shows a skeleton while loading, but still the trainer promo", () => {
    mockUseAuth.mockReturnValue(true);
    mockQuery({ data: undefined, isLoading: true });
    render(<HomeStats />);
    expect(screen.getByText("Ваша активность")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /открыть тренажёр/i })).toHaveAttribute(
      "href",
      "/trainer",
    );
    // данные ещё не загружены → блок статистики не отрисован
    expect(screen.queryByText("Активность за 12 недель")).not.toBeInTheDocument();
  });

  it("shows an error state with the promo still present", () => {
    mockUseAuth.mockReturnValue(true);
    mockQuery({ data: undefined, error: new Error("boom") });
    render(<HomeStats />);
    expect(screen.getByText("Ваша активность")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /открыть тренажёр/i })).toBeInTheDocument();
    expect(screen.queryByText("Активность за 12 недель")).not.toBeInTheDocument();
  });

  it("renders streak, heatmap, KPI numbers and trainer promo on success", () => {
    mockUseAuth.mockReturnValue(true);
    mockQuery({ data: sampleActivity });
    render(<HomeStats />);

    // стрик
    expect(screen.getByText("7")).toBeInTheDocument();
    expect(screen.getByText(/лучшая серия: 12/)).toBeInTheDocument();
    // heatmap
    expect(screen.getByText("Активность за 12 недель")).toBeInTheDocument();
    // KPI: значения из totals + подписи
    expect(screen.getByText("28")).toBeInTheDocument();
    expect(screen.getByText("Уроков изучено")).toBeInTheDocument();
    expect(screen.getByText("Заданий принято")).toBeInTheDocument();
    expect(screen.getByText("680")).toBeInTheDocument();
    // промо тренажёра
    expect(screen.getByRole("link", { name: /открыть тренажёр/i })).toHaveAttribute(
      "href",
      "/trainer",
    );
  });

  it("renders zeros for a brand-new user without activity (no crash)", () => {
    mockUseAuth.mockReturnValue(true);
    mockQuery({
      data: { days: [], streak: { current: 0, longest: 0 }, totals: { totalXp: 0, materialsCompleted: 0, issuesApproved: 0 } },
    });
    render(<HomeStats />);
    // стрик + 3 KPI-плитки — все нули, без падения
    expect(screen.getAllByText("0").length).toBeGreaterThanOrEqual(4);
    expect(screen.getByText("Активность за 12 недель")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /открыть тренажёр/i })).toBeInTheDocument();
  });
});
