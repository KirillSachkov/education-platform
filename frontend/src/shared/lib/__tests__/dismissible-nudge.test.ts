import { describe, it, expect, beforeEach } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useDismissibleNudge } from "../dismissible-nudge";

const DAY_MS = 1000 * 60 * 60 * 24;

// jsdom's localStorage in this setup lacks a working `clear`, so back it with a
// deterministic in-memory Storage for these tests.
function createStorageMock(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => {
      store.delete(k);
    },
    setItem: (k: string, v: string) => {
      store.set(k, String(v));
    },
  };
}

// Each test uses a UNIQUE storageKey so the module-level once-per-load visit
// guard (a Set keyed by storageKey) never carries over between cases.
let counter = 0;
const freshKey = () => `test-nudge-${(counter += 1)}`;

// Let the 0ms reveal timer fire inside act() — avoids "not wrapped in act" noise
// without resorting to fake timers (which interfere with React's scheduler).
async function flushReveal() {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, 5));
  });
}

describe("useDismissibleNudge", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      value: createStorageMock(),
      configurable: true,
    });
  });

  it("shows on a fresh load when minVisits is 1", async () => {
    const { result } = renderHook(() => useDismissibleNudge({ storageKey: freshKey() }));
    await flushReveal();
    expect(result.current.show).toBe(true);
  });

  it("stays hidden on the first load when minVisits is 2", async () => {
    const { result } = renderHook(() =>
      useDismissibleNudge({ storageKey: freshKey(), minVisits: 2 }),
    );
    await flushReveal();
    expect(result.current.show).toBe(false);
  });

  it("shows once the recorded visit count reaches minVisits", async () => {
    const key = freshKey();
    window.localStorage.setItem(`${key}:visits`, "1"); // user already loaded once
    const { result } = renderHook(() => useDismissibleNudge({ storageKey: key, minVisits: 2 }));
    await flushReveal();
    expect(result.current.show).toBe(true);
  });

  it("stays hidden while a dismissal is within the suppression window", async () => {
    const key = freshKey();
    window.localStorage.setItem(`${key}:dismissed-at`, String(Date.now()));
    const { result } = renderHook(() => useDismissibleNudge({ storageKey: key }));
    await flushReveal();
    expect(result.current.show).toBe(false);
  });

  it("shows again after the suppression window has elapsed", async () => {
    const key = freshKey();
    window.localStorage.setItem(`${key}:dismissed-at`, String(Date.now() - 31 * DAY_MS));
    const { result } = renderHook(() => useDismissibleNudge({ storageKey: key }));
    await flushReveal();
    expect(result.current.show).toBe(true);
  });

  it("dismiss() hides the nudge and persists the suppression timestamp", async () => {
    const key = freshKey();
    const { result } = renderHook(() => useDismissibleNudge({ storageKey: key }));
    await flushReveal();
    expect(result.current.show).toBe(true);

    act(() => result.current.dismiss());

    expect(result.current.show).toBe(false);
    expect(window.localStorage.getItem(`${key}:dismissed-at`)).not.toBeNull();
  });
});
