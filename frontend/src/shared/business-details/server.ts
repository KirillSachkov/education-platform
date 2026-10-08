import "server-only";
import fs from "node:fs/promises";
import { connection } from "next/server";
import { z } from "zod";
import type { BusinessDetails } from "./types";

const text = z.string().min(1).max(256);
const schema = z
  .object({
    name: text,
    taxId: z.string().regex(/^\d{12}$/),
    registrationId: z.string().regex(/^\d{15}$/),
    addressLines: z.array(text).min(1).max(4),
    taxOffice: text,
    email: z.email(),
    hours: text,
    copyrightName: text,
  })
  .strict();

/** Read at request time; never embed real proprietor details in a source build. */
export async function loadBusinessDetails(): Promise<BusinessDetails | null> {
  await connection();
  const file = process.env.BUSINESS_DETAILS_FILE;
  if (!file) return null;

  let content: string;
  try {
    content = await fs.readFile(file, "utf-8");
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "ENOENT") return null;
    throw error;
  }

  let input: unknown;
  try {
    input = JSON.parse(content);
  } catch {
    throw new Error("Invalid business details runtime file");
  }
  const result = schema.safeParse(input);
  if (!result.success) throw new Error("Invalid business details runtime file");
  return result.data;
}
