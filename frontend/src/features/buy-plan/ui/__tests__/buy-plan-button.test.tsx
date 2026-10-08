import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PRICING_INTENT_STORAGE_KEY } from "@/shared/lib/pricing-intent";

const { push, buy, trackGrowthEvent, authState } = vi.hoisted(() => ({
  push: vi.fn(),
  buy: vi.fn(),
  trackGrowthEvent: vi.fn(),
  authState: { status: "unauthenticated" },
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}));
vi.mock("next-auth/react", () => ({
  useSession: vi.fn(() => ({ status: authState.status })),
}));
vi.mock("../../model/use-buy-plan", () => ({
  useBuyPlan: () => ({ buy, isPending: false }),
}));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));

import { BuyPlanButton } from "../buy-plan-button";

const INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";

describe("BuyPlanButton pricing intent", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    authState.status = "unauthenticated";
    window.sessionStorage.clear();
    vi.spyOn(globalThis.crypto, "randomUUID").mockReturnValue(INTENT_ID);
  });

  it("creates and tracks an anonymous pricing intent only on click, then navigates to login", () => {
    render(
      <BuyPlanButton
        planId="plan-42"
        planSlug="dotnet-fullstack"
        priceCents={199_000}
        currency="RUB"
      />,
    );

    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).toBeNull();
    expect(trackGrowthEvent).not.toHaveBeenCalled();
    expect(push).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: /Войти и оплатить 1.990 ₽/ }));

    const storedIntent = window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY);
    expect(storedIntent).not.toBeNull();
    if (!storedIntent) throw new Error("Expected a stored pricing intent");
    expect(JSON.parse(storedIntent)).toMatchObject({
      intentId: INTENT_ID,
      planSlug: "dotnet-fullstack",
    });
    expect(trackGrowthEvent).toHaveBeenNthCalledWith(
      1,
      {
        name: "plan_selected",
        properties: {
          plan_id: "dotnet-fullstack",
          placement: "pricing",
          correlation_id: INTENT_ID,
        },
      },
      { once: `pricing-plan:${INTENT_ID}` },
    );
    expect(trackGrowthEvent).toHaveBeenNthCalledWith(
      2,
      {
        name: "auth_started",
        properties: {
          flow: "checkout",
          correlation_id: INTENT_ID,
        },
      },
      { once: `pricing-auth-started:${INTENT_ID}` },
    );
    expect(push).toHaveBeenCalledWith(
      `/login?callbackUrl=${encodeURIComponent(
        `/pricing?plan=dotnet-fullstack&intent=${INTENT_ID}&resume=checkout`,
      )}`,
    );
    expect(buy).not.toHaveBeenCalled();
  });

  it("reuses a preserved intent for an authenticated manual retry", () => {
    authState.status = "authenticated";
    window.sessionStorage.setItem(
      PRICING_INTENT_STORAGE_KEY,
      JSON.stringify({
        intentId: INTENT_ID,
        planSlug: "dotnet-fullstack",
        createdAt: new Date().toISOString(),
      }),
    );

    render(
      <BuyPlanButton
        planId="plan-42"
        planSlug="dotnet-fullstack"
        priceCents={199_000}
        currency="RUB"
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /Оплатить 1.990 ₽/ }));

    expect(buy).toHaveBeenCalledWith({
      planId: "plan-42",
      planSlug: "dotnet-fullstack",
      correlationId: INTENT_ID,
    });
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
  });
});
