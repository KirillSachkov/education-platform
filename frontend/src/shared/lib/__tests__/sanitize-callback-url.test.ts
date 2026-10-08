import { describe, expect, it } from "vitest";

import { sanitizeCallbackUrl } from "../sanitize-callback-url";

describe("sanitizeCallbackUrl", () => {
  it("принимает внутренние path-absolute пути", () => {
    expect(sanitizeCallbackUrl("/courses/dotnet")).toBe("/courses/dotnet");
    expect(sanitizeCallbackUrl("/settings/integrations?tab=github#top")).toBe(
      "/settings/integrations?tab=github#top",
    );
  });

  it("пустое значение → fallback", () => {
    expect(sanitizeCallbackUrl(null)).toBe("/");
    expect(sanitizeCallbackUrl(undefined)).toBe("/");
    expect(sanitizeCallbackUrl("")).toBe("/");
    expect(sanitizeCallbackUrl(null, "/home")).toBe("/home");
  });

  it("отклоняет абсолютные URL и схемы", () => {
    expect(sanitizeCallbackUrl("https://evil.com")).toBe("/");
    expect(sanitizeCallbackUrl("http://evil.com/x")).toBe("/");
    expect(sanitizeCallbackUrl("javascript:alert(1)")).toBe("/");
    expect(sanitizeCallbackUrl("evil.com")).toBe("/");
    expect(sanitizeCallbackUrl("relative/path")).toBe("/");
  });

  it("отклоняет protocol-relative", () => {
    expect(sanitizeCallbackUrl("//evil.com")).toBe("/");
  });

  it("отклоняет backslash в любой позиции (нормализуется браузером в /)", () => {
    expect(sanitizeCallbackUrl("/\\evil.com")).toBe("/");
    expect(sanitizeCallbackUrl("\\\\evil.com")).toBe("/");
    expect(sanitizeCallbackUrl("/a\\b")).toBe("/");
  });
});
