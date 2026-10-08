import type { OnboardingStepDto } from "@/entities/plan-onboarding";
import { describe, expect, it } from "vitest";
import { deriveWizardView } from "../wizard-state";

const STEPS: ReadonlyArray<OnboardingStepDto> = [
  { id: "s1", stepType: "MARKDOWN", isSkippable: false, sortOrder: "a0", title: "Welcome", body: "..." },
  { id: "s2", stepType: "TELEGRAM", isSkippable: true, sortOrder: "a1", title: null, body: null },
  { id: "s3", stepType: "GITHUB", isSkippable: true, sortOrder: "a2", title: null, body: null },
  { id: "s4", stepType: "NOTIFICATIONS", isSkippable: true, sortOrder: "a3", title: null, body: null },
];

const ALL_DONE = {
  completedStepIds: ["s1", "s2"],
  skippedStepIds: ["s3", "s4"],
};

describe("deriveWizardView", () => {
  it("returns completion when currentStepId is null (all done)", () => {
    expect(deriveWizardView(STEPS, { currentStepId: null, ...ALL_DONE })).toEqual({
      kind: "completion",
    });
  });

  it("returns step at correct index when currentStepId matches", () => {
    const view = deriveWizardView(STEPS, {
      currentStepId: "s2",
      completedStepIds: ["s1"],
      skippedStepIds: [],
    });
    expect(view.kind).toBe("step");
    if (view.kind !== "step") return;
    expect(view.step.id).toBe("s2");
    expect(view.index).toBe(1);
    expect(view.total).toBe(4);
  });

  it("returns first step when currentStepId is the first one", () => {
    const view = deriveWizardView(STEPS, {
      currentStepId: "s1",
      completedStepIds: [],
      skippedStepIds: [],
    });
    expect(view.kind).toBe("step");
    if (view.kind !== "step") return;
    expect(view.index).toBe(0);
  });

  it("returns last step when currentStepId is the last one", () => {
    const view = deriveWizardView(STEPS, {
      currentStepId: "s4",
      completedStepIds: ["s1", "s2", "s3"],
      skippedStepIds: [],
    });
    expect(view.kind).toBe("step");
    if (view.kind !== "step") return;
    expect(view.index).toBe(3);
    expect(view.total).toBe(4);
  });

  it("falls back to first pending step when currentStepId is null but steps remain (#497)", () => {
    // Прод-кейс Konstantin: юзер проскипал «старый конец» (cursor=null), автор
    // добавил шаг — CompletionView рендерить нельзя, complete бьёт 409.
    const view = deriveWizardView(STEPS, {
      currentStepId: null,
      completedStepIds: ["s1"],
      skippedStepIds: ["s2", "s3"],
    });
    expect(view.kind).toBe("step");
    if (view.kind !== "step") return;
    expect(view.step.id).toBe("s4");
    expect(view.index).toBe(3);
  });

  it("falls back to first pending step when currentStepId points to deleted step (#497)", () => {
    const view = deriveWizardView(STEPS, {
      currentStepId: "deleted-step-id",
      completedStepIds: ["s1", "s2"],
      skippedStepIds: [],
    });
    expect(view.kind).toBe("step");
    if (view.kind !== "step") return;
    expect(view.step.id).toBe("s3");
  });

  it("falls back to completion when currentStepId points to deleted step and nothing pending", () => {
    const view = deriveWizardView(STEPS, { currentStepId: "deleted-step-id", ...ALL_DONE });
    expect(view.kind).toBe("completion");
  });

  it("returns completion for empty steps list", () => {
    expect(
      deriveWizardView([], { currentStepId: null, completedStepIds: [], skippedStepIds: [] }),
    ).toEqual({ kind: "completion" });
  });
});
