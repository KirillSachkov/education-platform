import type { PlanGrantDto } from "@/entities/access-plan";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { SubscriptionRenewalControls } from "../subscription-renewal-controls";

function createGrant(overrides: Partial<PlanGrantDto> = {}): PlanGrantDto {
  return {
    id: "grant-1",
    userId: "user-1",
    planId: "plan-1",
    source: "PURCHASE",
    sourceRef: "order-1",
    grantedAt: "2026-07-01T09:00:00Z",
    expiresAt: "2099-08-01T09:00:00Z",
    status: "ACTIVE",
    revokedAt: null,
    revokeReason: null,
    nextChargeAt: "2099-07-30T09:00:00Z",
    chargeFailureCount: 0,
    renewalGraceEndsAt: null,
    autoRenewalCancelledAt: null,
    accessEndsAt: "2099-08-01T09:00:00Z",
    ...overrides,
  };
}

describe("subscription closure", () => {
  it.each([
    {},
    { autoRenewalCancelledAt: "2099-07-01T09:00:00Z" },
    { chargeFailureCount: 3, renewalGraceEndsAt: "2099-08-08T09:00:00Z" },
  ])("shows historical access without exposing charge actions", (overrides) => {
    render(<SubscriptionRenewalControls grant={createGrant(overrides)} />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText(/Новых списаний не будет/)).toBeInTheDocument();
    expect(screen.getByText(/Доступ до/)).toHaveTextContent("01.08.2099");
    expect(screen.queryByText("Следующее списание")).not.toBeInTheDocument();
  });
});
