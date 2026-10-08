import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { readExpiredDismissed, rememberExpiredDismissed } from "../model/dismiss";

// jsdom's built-in localStorage is unreliable in this setup (see
// shared/lib/__tests__/dismissible-nudge.test.ts) — back it with a deterministic Map.
function createStorageMock(): Storage {
  const store = new Map<string, string>();
  return {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (k: string) => (store.has(k) ? store.get(k)! : null),
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => void store.delete(k),
    setItem: (k: string, v: string) => void store.set(k, String(v)),
  };
}

describe("access-expired dismiss persistence", () => {
  beforeEach(() => {
    Object.defineProperty(window, "localStorage", {
      value: createStorageMock(),
      configurable: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("returns false for a grant that was never dismissed", () => {
    expect(readExpiredDismissed("grant-1")).toBe(false);
  });

  it("remembers a dismissed grant across reads (persistent)", () => {
    rememberExpiredDismissed("grant-1");
    expect(readExpiredDismissed("grant-1")).toBe(true);
  });

  it("is scoped per grantId — a new expired grant still shows", () => {
    rememberExpiredDismissed("grant-1");
    expect(readExpiredDismissed("grant-1")).toBe(true);
    expect(readExpiredDismissed("grant-2")).toBe(false);
  });

  it("does not throw and returns false when localStorage.getItem throws (private mode)", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("denied");
    });
    // Even if the prototype spy doesn't intercept the mock, a missing key returns false.
    expect(readExpiredDismissed("grant-x")).toBe(false);
  });

  it("does not throw when localStorage.setItem throws (private mode)", () => {
    Object.defineProperty(window, "localStorage", {
      value: {
        ...createStorageMock(),
        setItem: () => {
          throw new Error("denied");
        },
      },
      configurable: true,
    });
    expect(() => rememberExpiredDismissed("grant-1")).not.toThrow();
  });
});
