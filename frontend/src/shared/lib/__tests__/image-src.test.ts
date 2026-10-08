import { describe, expect, it } from "vitest";
import { API_ORIGIN } from "@/shared/api";
import {
  buildContentImageSrcSet,
  isContentImageUrl,
  resolveImageUrl,
  WIDTHS,
} from "../image-src";

// Derive from the same source the helper uses (defaults to http://localhost).
const ORIGIN = API_ORIGIN;

describe("resolveImageUrl", () => {
  it("prefixes relative paths with API_ORIGIN", () => {
    expect(resolveImageUrl("/api/files/abc/content")).toBe(`${ORIGIN}/api/files/abc/content`);
  });

  it("passes absolute URLs through unchanged", () => {
    const abs = `${ORIGIN}/api/files/abc/content`;
    expect(resolveImageUrl(abs)).toBe(abs);
  });

  it("passes external URLs through unchanged", () => {
    expect(resolveImageUrl("https://kinescope.io/poster.jpg")).toBe(
      "https://kinescope.io/poster.jpg",
    );
  });

  it("returns null for empty input", () => {
    expect(resolveImageUrl(null)).toBeNull();
    expect(resolveImageUrl(undefined)).toBeNull();
    expect(resolveImageUrl("")).toBeNull();
  });
});

describe("isContentImageUrl", () => {
  it("matches relative content paths", () => {
    expect(isContentImageUrl("/api/files/abc/content")).toBe(true);
  });

  it("matches absolute content URLs", () => {
    expect(isContentImageUrl(`${ORIGIN}/files/abc/content`)).toBe(true);
  });

  it("matches content URLs that already have a query", () => {
    expect(isContentImageUrl("/api/files/abc/content?v=2")).toBe(true);
  });

  it("rejects external URLs", () => {
    expect(isContentImageUrl("https://kinescope.io/poster.jpg")).toBe(false);
    expect(isContentImageUrl("https://avatars.githubusercontent.com/u/1")).toBe(false);
  });

  it("rejects a foreign host that mimics our content path (origin anchor)", () => {
    // Substring-only matching would have let this through and pointed a
    // generated srcSet at the attacker host. The origin anchor rejects it.
    expect(isContentImageUrl("https://evil.com/files/abc/content")).toBe(false);
    expect(isContentImageUrl("https://evil.com/api/files/abc/content")).toBe(false);
  });

  it("rejects non-content file paths and blobs", () => {
    expect(isContentImageUrl("/api/files/abc/download")).toBe(false);
    expect(isContentImageUrl("blob:http://localhost/uuid")).toBe(false);
  });

  it("rejects null", () => {
    expect(isContentImageUrl(null)).toBe(false);
    expect(isContentImageUrl(undefined)).toBe(false);
  });
});

describe("buildContentImageSrcSet", () => {
  it("builds a width srcSet for a relative content URL", () => {
    const srcSet = buildContentImageSrcSet("/api/files/abc/content");
    expect(srcSet).toBe(
      WIDTHS.map((w) => `${ORIGIN}/api/files/abc/content?w=${w} ${w}w`).join(", "),
    );
  });

  it("builds a srcSet for an already-absolute content URL", () => {
    const base = `${ORIGIN}/files/abc/content`;
    const srcSet = buildContentImageSrcSet(base);
    expect(srcSet).toBe(WIDTHS.map((w) => `${base}?w=${w} ${w}w`).join(", "));
  });

  it("appends with & when the URL already has a query", () => {
    const srcSet = buildContentImageSrcSet("/api/files/abc/content?v=2");
    expect(srcSet).toContain(`${ORIGIN}/api/files/abc/content?v=2&w=320 320w`);
    expect(srcSet).not.toContain("content?v=2?w=");
  });

  it("returns undefined for external URLs", () => {
    expect(buildContentImageSrcSet("https://kinescope.io/poster.jpg")).toBeUndefined();
  });

  it("returns undefined for a foreign host mimicking our content path", () => {
    expect(buildContentImageSrcSet("https://evil.com/files/abc/content")).toBeUndefined();
  });

  it("returns undefined for null/empty input", () => {
    expect(buildContentImageSrcSet(null)).toBeUndefined();
    expect(buildContentImageSrcSet(undefined)).toBeUndefined();
    expect(buildContentImageSrcSet("")).toBeUndefined();
  });
});
