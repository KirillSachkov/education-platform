// Operator pre-deploy check; no private documents are included in public source.
import fs from "node:fs/promises";
import path from "node:path";
import { CURRENT_LEGAL_VERSIONS } from "../src/shared/legal/versions.ts";

const markdownDir = process.env.LEGAL_DOCUMENTS_DIR || path.resolve("content/legal");
const pdfDir = process.env.LEGAL_PDFS_DIR || path.resolve("public/legal-docs");
for (const [slug, version] of Object.entries(CURRENT_LEGAL_VERSIONS)) {
  const name = `${slug}-${version}`;
  const markdown = await fs.readFile(path.join(markdownDir, `${name}.md`), "utf8");
  const pdf = await fs.readFile(path.join(pdfDir, `${name}.pdf`));
  if (!markdown.trim() || /⟦[^⟧]*⟧/.test(markdown)) {
    throw new Error(`Empty or unfinished legal document: ${name}`);
  }
  if (!pdf.subarray(0, 5).equals(Buffer.from("%PDF-"))) {
    throw new Error(`Missing PDF signature: ${name}`);
  }
}
process.stdout.write(`Current legal asset pairs verified: ${Object.keys(CURRENT_LEGAL_VERSIONS).length}\n`);
