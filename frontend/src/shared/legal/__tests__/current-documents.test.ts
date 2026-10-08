// @vitest-environment node
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CURRENT_LEGAL_VERSIONS, legalDocPdfHref } from "../versions";
import { loadLegalDocument, loadLegalPdf } from "../load-document";

vi.mock("server-only", () => ({}));
let directory: string;

beforeEach(async () => {
  directory = await fs.mkdtemp(path.join(os.tmpdir(), "legal-fixture-"));
  vi.stubEnv("LEGAL_DOCUMENTS_DIR", directory);
  vi.stubEnv("LEGAL_PDFS_DIR", directory);
});
afterEach(async () => {
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
  await fs.rm(directory, { recursive: true, force: true });
});

describe("owner-supplied legal documents", () => {
  it.each(Object.entries(CURRENT_LEGAL_VERSIONS))(
    "loads %s at its unchanged current version from the private runtime directory",
    async (slug, version) => {
      await fs.writeFile(path.join(directory, `${slug}-${version}.md`), "# Test fixture");
      const pdf = Buffer.from("%PDF-1.4\nTest fixture");
      await fs.writeFile(path.join(directory, `${slug}-${version}.pdf`), pdf);
      expect(await loadLegalDocument(slug, version)).toBe("# Test fixture");
      expect(await loadLegalPdf(slug, version)).toEqual(new Uint8Array(pdf));
      expect(legalDocPdfHref(slug as keyof typeof CURRENT_LEGAL_VERSIONS)).toBe(
        `/legal-docs/${slug}-${version}.pdf`,
      );
    },
  );

  it("reads historical versions and newly mounted files after an initial miss", async () => {
    expect(await loadLegalDocument("offer", "v1")).toBeNull();
    await fs.writeFile(path.join(directory, "offer-v1.md"), "# Archived fixture");
    expect(await loadLegalDocument("offer", "v1")).toBe("# Archived fixture");
  });

  it("uses the existing default directories when overrides are absent", async () => {
    vi.stubEnv("LEGAL_DOCUMENTS_DIR", "");
    vi.stubEnv("LEGAL_PDFS_DIR", "");
    vi.spyOn(process, "cwd").mockReturnValue(directory);
    await fs.mkdir(path.join(directory, "content/legal"), { recursive: true });
    await fs.writeFile(path.join(directory, "content/legal/offer-v2.md"), "# Default fixture");
    expect(await loadLegalDocument("offer", "v2")).toBe("# Default fixture");
    expect(await loadLegalPdf("offer", "v2")).toBeNull();
  });

  it.each([
    ["../offer", "v2"],
    ["offer", "../../secret"],
    ["toString", "v2"],
    ["unknown", "v1"],
  ])("rejects unsafe or unknown document %s/%s", async (slug, version) => {
    expect(await loadLegalDocument(slug, version)).toBeNull();
    expect(await loadLegalPdf(slug, version)).toBeNull();
  });

  it("does not hide an unreadable mount as a missing document", async () => {
    await fs.mkdir(path.join(directory, "offer-v2.md"));
    await expect(loadLegalDocument("offer", "v2")).rejects.toMatchObject({ code: "EISDIR" });
  });
});
