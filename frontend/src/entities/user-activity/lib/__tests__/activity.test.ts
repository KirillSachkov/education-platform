import { describe, expect, it } from "vitest";
import type { ActivityDayDto } from "../../types";
import {
  ACTIVITY_INTENSITY_CLASSES,
  activityIntensityIndex,
  buildRecentStrip,
  dayScore,
  isDayActive,
} from "../activity";

const day = (date: string, xp: number, materialsCompleted = 0): ActivityDayDto => ({
  date,
  xp,
  materialsCompleted,
});

describe("activityIntensityIndex", () => {
  it("returns 0 for no activity", () => {
    expect(activityIntensityIndex(0)).toBe(0);
  });

  it("returns 0 for negative score (defensive)", () => {
    expect(activityIntensityIndex(-5)).toBe(0);
  });

  it("returns 1 across the low band (1..15)", () => {
    expect(activityIntensityIndex(1)).toBe(1);
    expect(activityIntensityIndex(15)).toBe(1);
  });

  it("returns 2 across the mid band (16..45)", () => {
    expect(activityIntensityIndex(16)).toBe(2);
    expect(activityIntensityIndex(45)).toBe(2);
  });

  it("returns 3 above the mid band", () => {
    expect(activityIntensityIndex(46)).toBe(3);
    expect(activityIntensityIndex(1000)).toBe(3);
  });

  it("every index maps to an intensity class", () => {
    for (let s = -1; s <= 100; s += 7) {
      expect(ACTIVITY_INTENSITY_CLASSES[activityIntensityIndex(s)]).toBeTruthy();
    }
  });
});

describe("dayScore / isDayActive", () => {
  it("dayScore sums xp and materials", () => {
    expect(dayScore(day("2026-06-01", 10, 2))).toBe(12);
  });

  it("inactive when both zero", () => {
    expect(isDayActive(day("2026-06-01", 0, 0))).toBe(false);
  });

  it("active on xp only", () => {
    expect(isDayActive(day("2026-06-01", 10, 0))).toBe(true);
  });

  it("active on materials only", () => {
    expect(isDayActive(day("2026-06-01", 0, 1))).toBe(true);
  });
});

describe("buildRecentStrip", () => {
  it("returns the last n days in order with active flags", () => {
    const days = [day("2026-06-01", 0), day("2026-06-02", 10), day("2026-06-03", 0)];
    const strip = buildRecentStrip(days, 2);
    expect(strip).toHaveLength(2);
    expect(strip.map((d) => d.date)).toEqual(["2026-06-02", "2026-06-03"]);
    expect(strip.map((d) => d.active)).toEqual([true, false]);
  });

  it("handles an empty series", () => {
    expect(buildRecentStrip([], 7)).toEqual([]);
  });

  it("returns fewer than n when the series is short", () => {
    expect(buildRecentStrip([day("2026-06-01", 5)], 7)).toHaveLength(1);
  });

  it("defaults to a 7-day window", () => {
    const days = Array.from({ length: 84 }, (_, i) => day(`2026-01-${(i % 28) + 1}`, i));
    expect(buildRecentStrip(days)).toHaveLength(7);
  });

  it("labels weekdays in ru-short (Monday → «пн», Sunday → «вс»)", () => {
    expect(buildRecentStrip([day("2026-06-01", 5)], 7)[0].weekday).toBe("пн");
    expect(buildRecentStrip([day("2026-06-07", 5)], 7)[0].weekday).toBe("вс");
  });
});
