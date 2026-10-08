import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { PRICING_INTENT_STORAGE_KEY } from "@/shared/lib/pricing-intent";

const { createOrder, trackGrowthEvent } = vi.hoisted(() => ({
  createOrder: vi.fn(),
  trackGrowthEvent: vi.fn(),
}));

vi.mock("@/entities/access-order", () => ({
  accessOrderApi: { createOrder },
}));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));
vi.mock("@/shared/api", () => ({
  getErrorMessage: (_error: unknown, fallback: string) => fallback,
}));
vi.mock("sonner", () => ({ toast: { error: vi.fn() } }));

import { useBuyPlan } from "../use-buy-plan";

const INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";
const realLocation = window.location;

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe("useBuyPlan pricing correlation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    delete (window as { location?: Location }).location;
    (window as unknown as { location: { href: string } }).location = { href: "" };
  });

  afterEach(() => {
    (window as unknown as { location: Location }).location = realLocation;
  });

  it("tracks checkout only when create-order starts and uses the intent as idempotency key", async () => {
    createOrder.mockResolvedValue({
      result: { orderId: "order-1", paymentUrl: "https://pay.example/order-1" },
    });
    const { result } = renderHook(() => useBuyPlan(), { wrapper });

    expect(trackGrowthEvent).not.toHaveBeenCalled();
    act(() => {
      result.current.buy({
        planId: "plan-42",
        planSlug: "dotnet-fullstack",
        correlationId: INTENT_ID,
      });
    });

    await waitFor(() => {
      expect(createOrder).toHaveBeenCalledTimes(1);
    });
    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "checkout_started",
        properties: { plan_id: "dotnet-fullstack", correlation_id: INTENT_ID },
      },
      { once: `pricing-checkout:${INTENT_ID}` },
    );
    expect(createOrder).toHaveBeenCalledWith({ planId: "plan-42" }, INTENT_ID);
  });

  it("preserves the pricing intent when create-order fails", async () => {
    window.sessionStorage.setItem(
      PRICING_INTENT_STORAGE_KEY,
      JSON.stringify({
        intentId: INTENT_ID,
        planSlug: "dotnet-fullstack",
        createdAt: new Date().toISOString(),
      }),
    );
    createOrder.mockRejectedValue(new Error("network"));
    const { result } = renderHook(() => useBuyPlan(), { wrapper });

    act(() => {
      result.current.buy({
        planId: "plan-42",
        planSlug: "dotnet-fullstack",
        correlationId: INTENT_ID,
      });
    });

    await waitFor(() => {
      expect(result.current.isPending).toBe(false);
    });
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
    expect(window.location.href).toBe("");
  });
});
