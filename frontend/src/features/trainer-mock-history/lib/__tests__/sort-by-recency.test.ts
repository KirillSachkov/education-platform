import { describe, expect, it } from "vitest";

import { sortSessionsByRecency } from "../sort-by-recency";

type Row = { id: string; completedAt: string | null; startedAt: string };

describe("sortSessionsByRecency (#664)", () => {
  it("orders newest-first by displayed time (completedAt ?? startedAt), not by score", () => {
    // Бэкенд отдаёт по startedAt, но карточка показывает completedAt — раньше выглядело «по баллу».
    const rows: Row[] = [
      { id: "a", completedAt: "2026-06-26T20:45:00Z", startedAt: "2026-06-26T20:40:00Z" },
      { id: "b", completedAt: "2026-06-26T20:56:00Z", startedAt: "2026-06-26T20:50:00Z" },
      { id: "c", completedAt: "2026-06-26T20:30:00Z", startedAt: "2026-06-26T20:25:00Z" },
    ];

    expect(sortSessionsByRecency(rows).map((r) => r.id)).toEqual(["b", "a", "c"]);
  });

  it("falls back to startedAt when a session is not yet completed", () => {
    const rows: Row[] = [
      { id: "done", completedAt: "2026-06-26T20:30:00Z", startedAt: "2026-06-26T20:10:00Z" },
      { id: "in-progress", completedAt: null, startedAt: "2026-06-26T20:50:00Z" },
    ];

    expect(sortSessionsByRecency(rows).map((r) => r.id)).toEqual(["in-progress", "done"]);
  });

  it("does not mutate the input array", () => {
    const rows: Row[] = [
      { id: "a", completedAt: "2026-06-26T20:10:00Z", startedAt: "2026-06-26T20:00:00Z" },
      { id: "b", completedAt: "2026-06-26T20:20:00Z", startedAt: "2026-06-26T20:15:00Z" },
    ];
    const original = rows.map((r) => r.id);

    sortSessionsByRecency(rows);
    expect(rows.map((r) => r.id)).toEqual(original);
  });
});
