import { routes } from "@/shared/config/routes";
import { describe, expect, it } from "vitest";
import { resolveNextPath } from "../next-path";

describe("resolveNextPath — open-redirect защита ?next=", () => {
  it("accepts an internal path", () => {
    expect(resolveNextPath("/courses/net-fullstack")).toBe("/courses/net-fullstack");
  });

  it("accepts an internal path with query and hash", () => {
    expect(resolveNextPath("/knowledge-base?tag=dotnet#top")).toBe("/knowledge-base?tag=dotnet#top");
  });

  it.each([null, undefined, ""])("falls back to home when next is %s", (value) => {
    expect(resolveNextPath(value)).toBe(routes.home);
  });

  it.each([
    "https://evil.com",
    "http://evil.com/phish",
    "javascript:alert(1)",
    "//evil.com",
    "//evil.com/path",
    "/\\evil.com",
    "\\\\evil.com",
    "evil.com",
    "relative/path",
  ])("falls back to home for external/malicious next %s", (value) => {
    expect(resolveNextPath(value)).toBe(routes.home);
  });
});
