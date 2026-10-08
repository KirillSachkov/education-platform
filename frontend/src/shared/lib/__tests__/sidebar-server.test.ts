import { beforeEach, describe, expect, it, vi } from "vitest";

const getCookie = vi.fn();

vi.mock("next/headers", () => ({
  cookies: vi.fn(async () => ({ get: getCookie })),
}));

import { readSidebarDefaultOpen } from "@/shared/lib/sidebar-server";

describe("readSidebarDefaultOpen", () => {
  beforeEach(() => {
    getCookie.mockReset();
  });

  it("defaults to open when the cookie is absent", async () => {
    getCookie.mockReturnValue(undefined);
    expect(await readSidebarDefaultOpen()).toBe(true);
  });

  it("returns false only when the cookie value is exactly 'false'", async () => {
    getCookie.mockReturnValue({ value: "false" });
    expect(await readSidebarDefaultOpen()).toBe(false);
  });

  it("returns open for a 'true' cookie value", async () => {
    getCookie.mockReturnValue({ value: "true" });
    expect(await readSidebarDefaultOpen()).toBe(true);
  });
});
