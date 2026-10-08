import { myOrdersQueryOptions, type ListMyOrdersResponse } from "@/entities/access-order";
import { myGrantsQueryKey, type PlanCapability, type PlanGrantDto } from "@/entities/access-plan";
import type { Envelope } from "@/shared/api";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MyPlansView } from "../my-plans-view";

const FULL_CAPABILITIES: PlanCapability[] = [
  "VIEW_MATERIALS",
  "SUBMIT_ISSUES",
  "CODE_REVIEW",
  "COMMUNITY_ACCESS",
  "LIVE_CALLS",
  "JOB_SUPPORT",
];

function createGrant(
  id: string,
  displayName: string,
  tier: "FULL_ALL" | "LEARN_ALL",
  expiresAt: string | null,
  capabilities: PlanCapability[] = FULL_CAPABILITIES,
  trialDurationDays: number | null = expiresAt === null ? null : 30,
): PlanGrantDto {
  return {
    id,
    userId: "user-1",
    planId: `plan-${id}`,
    source: "PURCHASE",
    sourceRef: `order-${id}`,
    grantedAt: "2026-07-15T15:03:46Z",
    expiresAt,
    status: "ACTIVE",
    revokedAt: null,
    revokeReason: null,
    nextChargeAt: null,
    chargeFailureCount: 0,
    renewalGraceEndsAt: null,
    autoRenewalCancelledAt: null,
    accessEndsAt: expiresAt,
    plan: {
      id: `plan-${id}`,
      authorId: "author-1",
      slug: `plan-${id}`,
      displayName,
      shortDescription: null,
      tier,
      coverFileId: null,
      capabilities,
      courseIds: [],
      includesFutureContent: true,
      hasOnboardingEnabled: false,
      trialDurationDays,
    },
  };
}

function renderMyPlans(grants: PlanGrantDto[]) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Number.POSITIVE_INFINITY } },
  });
  queryClient.setQueryData<Envelope<PlanGrantDto[]>>(myGrantsQueryKey, {
    result: grants,
    error: null,
    isError: false,
    timeGenerated: "2026-07-15T15:04:00Z",
  });
  queryClient.setQueryData<Envelope<ListMyOrdersResponse>>(
    myOrdersQueryOptions({ status: "PAID", pageSize: 50 }).queryKey,
    {
      result: { items: [], total: 0, page: 1, pageSize: 50 },
      error: null,
      isError: false,
      timeGenerated: "2026-07-15T15:04:00Z",
    },
  );

  return render(
    <QueryClientProvider client={queryClient}>
      <MyPlansView />
    </QueryClientProvider>,
  );
}

describe("MyPlansView upgrade visibility", () => {
  it("shows only permanent full access after a paid month-to-lifetime upgrade", () => {
    renderMyPlans([
      createGrant("lifetime", "Полный доступ", "FULL_ALL", null),
      createGrant("month", "Полный доступ на месяц", "FULL_ALL", "2026-07-20T09:53:35Z"),
    ]);

    expect(screen.getByRole("heading", { name: "Полный доступ" })).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Полный доступ на месяц" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/Доступ активен до/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /Доплатить до полного доступа/ }),
    ).not.toBeInTheDocument();
  });

  it("keeps the upgrade banner and month card for trial-only access", () => {
    renderMyPlans([
      createGrant("month", "Полный доступ на месяц", "FULL_ALL", "2026-07-20T09:53:35Z"),
    ]);

    expect(screen.getByRole("heading", { name: "Полный доступ на месяц" })).toBeInTheDocument();
    expect(screen.getByText(/Доступ активен до/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Доплатить до полного доступа/ })).toBeInTheDocument();
  });

  it("keeps trial behavior while an older API response omits trial duration", () => {
    const legacyTrial = createGrant(
      "month",
      "Полный доступ на месяц",
      "FULL_ALL",
      "2026-07-20T09:53:35Z",
    );
    delete legacyTrial.plan?.trialDurationDays;

    renderMyPlans([legacyTrial]);

    expect(screen.getByRole("heading", { name: "Полный доступ на месяц" })).toBeInTheDocument();
    expect(screen.getByText(/Доступ активен до/)).toBeInTheDocument();
  });

  it("does not let permanent learn-only access hide a broader monthly full grant", () => {
    renderMyPlans([
      createGrant("learn", "Все материалы", "LEARN_ALL", null, ["VIEW_MATERIALS"]),
      createGrant("month", "Полный доступ на месяц", "FULL_ALL", "2026-07-20T09:53:35Z"),
    ]);

    expect(screen.getByRole("heading", { name: "Все материалы" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Полный доступ на месяц" })).toBeInTheDocument();
    expect(screen.getByText(/Доступ активен до/)).toBeInTheDocument();
  });

  it("does not hide an unrelated expiring full grant without trial semantics", () => {
    renderMyPlans([
      createGrant("lifetime", "Полный доступ", "FULL_ALL", null),
      createGrant(
        "temporary",
        "Временный полный доступ",
        "FULL_ALL",
        "2026-07-20T09:53:35Z",
        FULL_CAPABILITIES,
        null,
      ),
    ]);

    expect(screen.getByRole("heading", { name: "Полный доступ" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Временный полный доступ" })).toBeInTheDocument();
    expect(screen.queryByText(/Доступ активен до/)).not.toBeInTheDocument();
  });
});
