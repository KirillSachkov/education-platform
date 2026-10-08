import "server-only";
import fs from "node:fs/promises";
import path from "node:path";
import { isLegalDocSlug } from "./versions";

async function readLegalFile(slug: string, version: string, format: "md" | "pdf") {
  if (!isLegalDocSlug(slug) || !/^v\d+$/.test(version)) return null;

  const directory =
    format === "md"
      ? process.env.LEGAL_DOCUMENTS_DIR || path.join(process.cwd(), "content", "legal")
      : process.env.LEGAL_PDFS_DIR || path.join(process.cwd(), "public", "legal-docs");
  try {
    return await fs.readFile(path.join(directory, `${slug}-${version}.${format}`));
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return null;
    throw error;
  }
}

/** Read owner-supplied terms at runtime; absent documents retain the existing 404 behavior. */
export async function loadLegalDocument(slug: string, version: string): Promise<string | null> {
  const content = await readLegalFile(slug, version, "md");
  return content?.toString("utf-8") ?? null;
}

export async function loadLegalPdf(
  slug: string,
  version: string,
): Promise<Uint8Array<ArrayBuffer> | null> {
  const content = await readLegalFile(slug, version, "pdf");
  return content ? new Uint8Array(content) : null;
}
