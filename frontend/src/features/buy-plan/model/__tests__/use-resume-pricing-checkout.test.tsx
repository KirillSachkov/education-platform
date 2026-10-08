import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PRICING_INTENT_STORAGE_KEY, PRICING_INTENT_TTL_MS } from "@/shared/lib/pricing-intent";

const { buy, queryState } = vi.hoisted(() => ({
  buy: vi.fn(),
  queryState: { value: "" },
}));

vi.mock("next/navigation", () => ({
  useSearchParams: () => new URLSearchParams(queryState.value),
}));
vi.mock("../use-buy-plan", () => ({
  useBuyPlan: () => ({ buy, isPending: false }),
}));

import { useResumePricingCheckout } from "../use-resume-pricing-checkout";

const INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";
const PLANS = [{ id: "plan-42", slug: "dotnet-fullstack" }];

function storeIntent(overrides: Record<string, unknown> = {}) {
  window.sessionStorage.setItem(
    PRICING_INTENT_STORAGE_KEY,
    JSON.stringify({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: new Date().toISOString(),
      ...overrides,
    }),
  );
}

function renderResume(publicPlans = PLANS) {
  return renderHook(() => {
    useResumePricingCheckout({
      publicPlans,
      isLoading: false,
      isAuthenticated: true,
      billingEnabled: true,
    });
  });
}

describe("useResumePricingCheckout", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
    document.body.replaceChildren();
    queryState.value = `plan=dotnet-fullstack&intent=${INTENT_ID}&resume=checkout`;
  });

  it("auto-starts checkout once when query, storage and fetched public plan match", async () => {
    storeIntent();

    const first = renderResume();
    await waitFor(() => {
      expect(buy).toHaveBeenCalledWith({
        planId: "plan-42",
        planSlug: "dotnet-fullstack",
        correlationId: INTENT_ID,
      });
    });

    first.rerender();
    first.unmount();
    renderResume();

    await waitFor(() => {
      expect(buy).toHaveBeenCalledTimes(1);
    });
  });

  it("only scrolls to the fetched plan for a forged intent and never auto-buys", async () => {
    storeIntent();
    queryState.value = `plan=dotnet-fullstack&intent=${INTENT_ID.slice(0, -1)}2&resume=checkout`;
    const planElement = document.createElement("section");
    planElement.id = "dotnet-fullstack";
    const scrollIntoView = vi.fn();
    planElement.scrollIntoView = scrollIntoView;
    document.body.append(planElement);

    renderResume();

    await waitFor(() => {
      expect(scrollIntoView).toHaveBeenCalled();
    });
    expect(buy).not.toHaveBeenCalled();
  });

  it.each(["missing", "stale"] as const)(
    "only scrolls to the fetched plan for a %s stored intent",
    async (state) => {
      if (state === "stale") {
        storeIntent({
          createdAt: new Date(Date.now() - PRICING_INTENT_TTL_MS - 1).toISOString(),
        });
      }
      const planElement = document.createElement("section");
      planElement.id = "dotnet-fullstack";
      const scrollIntoView = vi.fn();
      planElement.scrollIntoView = scrollIntoView;
      document.body.append(planElement);

      renderResume();

      await waitFor(() => {
        expect(scrollIntoView).toHaveBeenCalled();
      });
      expect(buy).not.toHaveBeenCalled();
    },
  );

  it("never auto-buys a plan that is absent from the fetched public catalog", () => {
    storeIntent();

    renderResume([{ id: "plan-other", slug: "other-plan" }]);

    expect(buy).not.toHaveBeenCalled();
  });
});
