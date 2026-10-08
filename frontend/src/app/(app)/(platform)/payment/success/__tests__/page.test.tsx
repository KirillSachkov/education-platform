import { render, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PRICING_INTENT_STORAGE_KEY } from "@/shared/lib/pricing-intent";

const { queryState, trackGrowthEvent, clearLastOrderId, invalidateQueries } = vi.hoisted(() => ({
  queryState: { status: "PENDING", isError: false },
  trackGrowthEvent: vi.fn(),
  clearLastOrderId: vi.fn(),
  invalidateQueries: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams({ OrderId: "order-1" }),
}));
vi.mock("@/entities/access-order", () => ({
  orderStatusQueryOptions: (orderId: string | null) => ({ queryKey: ["order", orderId] }),
}));
vi.mock("@/entities/plan-onboarding", () => ({
  currentOnboardingQueryOptions: { queryKey: ["current-onboarding"] },
}));
vi.mock("@/features/buy-plan", () => ({
  clearLastOrderId,
  readLastOrderId: () => null,
}));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));
vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({
    data: { status: queryState.status, failureReason: null },
    isError: queryState.isError,
  }),
  useQueryClient: () => ({ invalidateQueries }),
}));

import PaymentSuccessPage from "../page";

const INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";

function storeIntent() {
  window.sessionStorage.setItem(
    PRICING_INTENT_STORAGE_KEY,
    JSON.stringify({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: new Date().toISOString(),
    }),
  );
}

describe("PaymentSuccessPage growth purchase", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    queryState.status = "PENDING";
    queryState.isError = false;
  });

  it("emits purchase_success once and consumes the intent only after backend status PAID", async () => {
    storeIntent();
    const view = render(<PaymentSuccessPage />);

    expect(trackGrowthEvent).not.toHaveBeenCalled();
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();

    queryState.status = "PAID";
    view.rerender(<PaymentSuccessPage />);

    await waitFor(() => {
      expect(trackGrowthEvent).toHaveBeenCalledWith(
        {
          name: "purchase_success",
          properties: {
            plan_id: "dotnet-fullstack",
            correlation_id: INTENT_ID,
          },
        },
        { once: `pricing-purchase:${INTENT_ID}` },
      );
    });
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).toBeNull();
  });

  it("does not emit or consume the intent for FAILED", async () => {
    storeIntent();
    queryState.status = "FAILED";

    render(<PaymentSuccessPage />);

    await waitFor(() => {
      expect(clearLastOrderId).toHaveBeenCalled();
    });
    expect(trackGrowthEvent).not.toHaveBeenCalled();
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
  });
});
