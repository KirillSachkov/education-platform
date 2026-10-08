import { describe, expect, it } from "vitest";

import {
  formatTrainerClock,
  MAX_VOICE_ANSWER_LABEL,
  MAX_VOICE_ANSWER_SECONDS,
} from "@/shared/config/trainer";

describe("trainer voice-answer duration cap (#663)", () => {
  it("caps a voice answer at 3 minutes with a matching human label", () => {
    expect(MAX_VOICE_ANSWER_SECONDS).toBe(180);
    expect(MAX_VOICE_ANSWER_LABEL).toBe("3 мин");
  });

  it("formatTrainerClock renders m:ss, floors fractions, and clamps negatives", () => {
    expect(formatTrainerClock(0)).toBe("0:00");
    expect(formatTrainerClock(5)).toBe("0:05");
    expect(formatTrainerClock(75)).toBe("1:15");
    expect(formatTrainerClock(MAX_VOICE_ANSWER_SECONDS)).toBe("3:00");
    expect(formatTrainerClock(59.9)).toBe("0:59");
    expect(formatTrainerClock(-5)).toBe("0:00");
  });
});
