import { describe, expect, it } from "vitest";
import {
  buildFileMarkdown,
  buildImageMarkdown,
  extractAssetIds,
  formatFileSize,
  getFileExtension,
  inferContentType,
  isFileAssetHref,
  isImageFile,
} from "../markdown-assets";

const FAKE_UUID = "11111111-2222-3333-4444-555555555555";

describe("buildFileMarkdown / extractAssetIds round-trip", () => {
  it("includes formatted size suffix and extracts uuid back", () => {
    const md = buildFileMarkdown("report.pdf", FAKE_UUID, 234 * 1024);
    expect(md).toBe(`[report.pdf (234 КБ)](/files/${FAKE_UUID}/content)`);
    expect(extractAssetIds(md)).toEqual([FAKE_UUID]);
  });

  it("omits size suffix when bytes not provided", () => {
    const md = buildFileMarkdown("a.txt", FAKE_UUID);
    expect(md).toBe(`[a.txt](/files/${FAKE_UUID}/content)`);
  });

  it("strips brackets and parens from filename to keep markdown link valid", () => {
    const md = buildFileMarkdown("report (final) [v2].pdf", FAKE_UUID, 1024);
    expect(md).not.toContain("(final)");
    expect(md).not.toContain("[v2]");
    expect(extractAssetIds(md)).toEqual([FAKE_UUID]);
  });

  it("extracts ids from mixed image + file markdown", () => {
    const id1 = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    const id2 = "ffffffff-1111-2222-3333-444444444444";
    const md =
      buildImageMarkdown("a.png", id1, { width: 100, height: 50 }) +
      "\n\n" +
      buildFileMarkdown("b.pdf", id2, 2048);
    expect(extractAssetIds(md).sort()).toEqual([id1, id2].sort());
  });
});

describe("isFileAssetHref", () => {
  it("matches anchored /files/{uuid}/content paths", () => {
    expect(isFileAssetHref(`/files/${FAKE_UUID}/content`)).toBe(true);
  });

  it("rejects external lookalike URLs (anchor at end of string)", () => {
    expect(isFileAssetHref(`https://evil.com/files/${FAKE_UUID}/content/x`)).toBe(false);
    expect(isFileAssetHref(`https://evil.com/wrap/files/${FAKE_UUID}/content`)).toBe(true);
  });

  it("rejects empty / unrelated hrefs", () => {
    expect(isFileAssetHref(undefined)).toBe(false);
    expect(isFileAssetHref(null)).toBe(false);
    expect(isFileAssetHref("")).toBe(false);
    expect(isFileAssetHref("https://example.com")).toBe(false);
    expect(isFileAssetHref("javascript:alert(1)")).toBe(false);
  });
});

describe("inferContentType", () => {
  it("maps .excalidraw to application/vnd.excalidraw+json regardless of file.type", () => {
    const f = new File(["{}"], "diagram.excalidraw", { type: "" });
    expect(inferContentType(f)).toBe("application/vnd.excalidraw+json");
  });

  it("maps .md / .markdown to text/markdown", () => {
    expect(inferContentType(new File([""], "n.md", { type: "" }))).toBe("text/markdown");
    expect(inferContentType(new File([""], "n.MARKDOWN", { type: "" }))).toBe("text/markdown");
  });

  it("falls back to file.type for unknown extensions", () => {
    expect(inferContentType(new File([""], "x.bin", { type: "application/pdf" }))).toBe(
      "application/pdf",
    );
  });

  it("falls back to octet-stream when extension and type both unknown", () => {
    expect(inferContentType(new File([""], "noext", { type: "" }))).toBe(
      "application/octet-stream",
    );
  });
});

describe("formatFileSize", () => {
  it("formats bytes / KB / MB / GB", () => {
    expect(formatFileSize(0)).toBe("0 Б");
    expect(formatFileSize(512)).toBe("512 Б");
    expect(formatFileSize(2048)).toBe("2 КБ");
    expect(formatFileSize(2 * 1024 * 1024)).toBe("2.0 МБ");
    expect(formatFileSize(3 * 1024 * 1024 * 1024)).toBe("3.00 ГБ");
  });
});

describe("getFileExtension", () => {
  it("returns lowercase extension without dot", () => {
    expect(getFileExtension("a.PDF")).toBe("pdf");
    expect(getFileExtension("a.tar.gz")).toBe("gz");
  });

  it("returns empty string when no extension", () => {
    expect(getFileExtension("Makefile")).toBe("");
    expect(getFileExtension("")).toBe("");
    expect(getFileExtension("trailing.")).toBe("");
  });
});

describe("isImageFile", () => {
  it("matches any image/* MIME", () => {
    expect(isImageFile(new File([""], "x.png", { type: "image/png" }))).toBe(true);
    expect(isImageFile(new File([""], "x.webp", { type: "image/webp" }))).toBe(true);
  });

  it("rejects non-image", () => {
    expect(isImageFile(new File([""], "x.pdf", { type: "application/pdf" }))).toBe(false);
    expect(isImageFile(new File([""], "x", { type: "" }))).toBe(false);
  });
});
