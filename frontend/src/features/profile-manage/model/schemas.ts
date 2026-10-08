import { z } from "zod";

// ── Schemas ─────────────────────────────────────────────────────

export const baseSchema = z.object({
  bio: z.string().max(1000, "Максимум 1000 символов").optional(),
});

export const authorSchema = z.object({
  specialization: z.string().max(255, "Максимум 255 символов").optional(),
  aboutAsAuthor: z.string().max(2000, "Максимум 2000 символов").optional(),
});

export const reviewerSchema = z.object({
  reviewCapacity: z
    .string()
    .optional()
    .refine((value) => {
      if (!value || value.trim().length === 0) return true;
      const parsed = Number(value);
      return Number.isInteger(parsed) && parsed >= 0;
    }, "Введите целое число не меньше 0"),
  expertise: z.string().max(500, "Максимум 500 символов").optional(),
});

// ── Form value types ────────────────────────────────────────────

export type BaseFormValues = z.infer<typeof baseSchema>;
export type AuthorFormValues = z.infer<typeof authorSchema>;
export type ReviewerFormValues = z.infer<typeof reviewerSchema>;

// ── Helpers ─────────────────────────────────────────────────────

export function normalizeText(value?: string | null): string | null {
  if (!value) return null;
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
}
