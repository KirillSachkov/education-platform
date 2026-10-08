import { describe, expect, expectTypeOf, it } from "vitest";
import {
  GROWTH_EVENT_REGISTRY,
  GROWTH_EVENT_NAMES,
  GROWTH_EVENT_VERSION,
  normalizeGrowthEventProperties,
  type GrowthEvent,
} from "../contract";

describe("growth.v1 event contract", () => {
  it("exposes the complete versioned event-name union", () => {
    expect(GROWTH_EVENT_VERSION).toBe("growth.v1");
    expect(GROWTH_EVENT_NAMES).toEqual([
      "landing_view",
      "cta_click",
      "catalog_view",
      "course_view",
      "free_material_open",
      "free_material_engaged",
      "level_test_started",
      "level_test_completed",
      "level_test_result_teaser",
      "level_test_auth_started",
      "level_test_claimed",
      "level_test_recommendation_clicked",
      "auth_started",
      "auth_completed",
      "plan_selected",
      "checkout_started",
      "purchase_success",
      "first_material_started",
      "first_material_completed",
      "first_issue_submitted",
      "continue_learning_click",
    ]);
    expectTypeOf<GrowthEvent["name"]>().toEqualTypeOf<(typeof GROWTH_EVENT_NAMES)[number]>();
  });

  it("defines trigger, owner, source and consent policy for every event", () => {
    expect(Object.keys(GROWTH_EVENT_REGISTRY)).toEqual(GROWTH_EVENT_NAMES);
    for (const definition of Object.values(GROWTH_EVENT_REGISTRY)) {
      expect(definition.trigger.length).toBeGreaterThan(0);
      expect(definition.owner.length).toBeGreaterThan(0);
      expect(["client", "server"]).toContain(definition.source);
      expect(["analytics", "necessary"]).toContain(definition.consent);
    }
  });

  it("keeps only event-specific safe properties and bounds string values", () => {
    const properties = normalizeGrowthEventProperties({
      name: "cta_click",
      properties: {
        cta_id: "final_consultation",
        placement: "footer",
        email: "person@example.com",
        user_id: "user-1",
      },
    } as unknown as GrowthEvent);

    expect(properties).toEqual({ cta_id: "final_consultation", placement: "footer" });
  });

  it("rejects unknown CTA ids and invalid id/placement combinations", () => {
    expect(
      normalizeGrowthEventProperties({
        name: "cta_click",
        properties: { cta_id: "dynamic", placement: "hero" },
      } as unknown as GrowthEvent),
    ).toEqual({});
    expect(
      normalizeGrowthEventProperties({
        name: "cta_click",
        properties: { cta_id: "hero_full_access", placement: "footer" },
      } as unknown as GrowthEvent),
    ).toEqual({});
  });

  it.each([
    "plan_selected",
    "auth_started",
    "auth_completed",
    "checkout_started",
    "purchase_success",
  ] as const)("keeps only a canonical UUID correlation_id on %s", (name) => {
    const correlationId = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";
    const baseProperties = name.startsWith("auth_")
      ? { flow: "checkout" as const }
      : { plan_id: "plan-42" };

    expect(
      normalizeGrowthEventProperties({
        name,
        properties: { ...baseProperties, correlation_id: correlationId },
      } as GrowthEvent),
    ).toMatchObject({ correlation_id: correlationId });

    expect(
      normalizeGrowthEventProperties({
        name,
        properties: { ...baseProperties, correlation_id: correlationId.toUpperCase() },
      } as GrowthEvent),
    ).not.toHaveProperty("correlation_id");
  });
});
