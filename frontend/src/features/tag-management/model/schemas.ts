import { z } from "zod";

export const tagTitleSchema = z
  .string()
  .min(1, "Название обязательно")
  .max(150, "Максимум 150 символов");

export const createTagSchema = z.object({
  title: tagTitleSchema,
});

export const updateTagSchema = z.object({
  title: tagTitleSchema,
});

export type CreateTagFormData = z.infer<typeof createTagSchema>;
export type UpdateTagFormData = z.infer<typeof updateTagSchema>;
