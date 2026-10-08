// @vitest-environment node
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import example from "../../../../business-details.example.json";
import { loadBusinessDetails } from "../server";

vi.mock("server-only", () => ({}));
vi.mock("next/server", () => ({ connection: vi.fn().mockResolvedValue(undefined) }));

const directories: string[] = [];
async function configure(content: string) {
  const directory = await fs.mkdtemp(path.join(os.tmpdir(), "business-details-test-"));
  directories.push(directory);
  const file = path.join(directory, "details.json");
  await fs.writeFile(file, content);
  vi.stubEnv("BUSINESS_DETAILS_FILE", file);
  return file;
}

afterEach(async () => {
  vi.restoreAllMocks();
  vi.unstubAllEnvs();
  await Promise.all(
    directories.splice(0).map((directory) => fs.rm(directory, { recursive: true })),
  );
});

describe("public business details from private runtime input", () => {
  it("omits details when no operator file is configured", async () => {
    vi.stubEnv("BUSINESS_DETAILS_FILE", undefined);
    expect(await loadBusinessDetails()).toBeNull();
  });

  it("omits a missing runtime file", async () => {
    const file = await configure(JSON.stringify(example));
    await fs.unlink(file);
    expect(await loadBusinessDetails()).toBeNull();
  });

  it("reads configured details at request time, including changes after a previous request", async () => {
    const file = await configure(JSON.stringify(example));
    expect(await loadBusinessDetails()).toEqual(example);
    const changed = { ...example, name: "Другой учебный пример" };
    await fs.writeFile(file, JSON.stringify(changed));
    expect(await loadBusinessDetails()).toEqual(changed);
  });

  it.each([
    "not JSON",
    JSON.stringify({ ...example, taxId: "invalid" }),
    JSON.stringify({ ...example, registrationId: "invalid" }),
    JSON.stringify({ ...example, addressLines: [] }),
    JSON.stringify({ ...example, email: "javascript:alert(1)" }),
    JSON.stringify({ ...example, privateKey: "must not reach a client" }),
  ])(
    "rejects malformed or unexpected configuration without exposing its content",
    async (content) => {
      await configure(content);
      await expect(loadBusinessDetails()).rejects.toThrow("Invalid business details runtime file");
    },
  );

  it("propagates a permission failure instead of hiding a broken mount", async () => {
    await configure(JSON.stringify(example));
    const error = Object.assign(new Error("permission denied"), { code: "EACCES" });
    vi.spyOn(fs, "readFile").mockRejectedValueOnce(error);
    await expect(loadBusinessDetails()).rejects.toBe(error);
  });
});
