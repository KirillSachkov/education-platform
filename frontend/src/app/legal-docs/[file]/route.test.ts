// @vitest-environment node
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { GET } from "./route";

vi.mock("server-only", () => ({}));
let directory: string;

beforeEach(async () => {
  directory = await fs.mkdtemp(path.join(os.tmpdir(), "legal-pdf-fixture-"));
  vi.stubEnv("LEGAL_PDFS_DIR", directory);
});
afterEach(async () => {
  vi.unstubAllEnvs();
  await fs.rm(directory, { recursive: true, force: true });
});

const request = new Request("http://localhost/legal-docs/offer-v1.pdf");
const get = (file: string) => GET(request, { params: Promise.resolve({ file }) });

it("downloads archived PDFs byte-for-byte at the existing URL", async () => {
  const pdf = Buffer.from("%PDF-1.4\nArchived test fixture");
  await fs.writeFile(path.join(directory, "offer-v1.pdf"), pdf);
  const response = await get("offer-v1.pdf");
  expect(response.status).toBe(200);
  expect(response.headers.get("content-type")).toBe("application/pdf");
  expect(response.headers.get("content-disposition")).toBe('attachment; filename="offer-v1.pdf"');
  expect(new Uint8Array(await response.arrayBuffer())).toEqual(new Uint8Array(pdf));
});

it.each(["offer-v2.pdf", "../offer-v1.pdf", 'offer-v1.pdf"', "unknown-v1.pdf"])(
  "returns 404 for missing or invalid file %s",
  async (file) => {
    expect((await get(file)).status).toBe(404);
  },
);
