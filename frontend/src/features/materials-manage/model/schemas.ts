import { z } from "zod";

export const articleEditorSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, "Введите название статьи")
    .max(200, "Максимум 200 символов"),
  content: z
    .string()
    .trim()
    .min(1, "Добавьте содержимое статьи")
    .max(10000, "Максимум 10000 символов"),
  isPublic: z.boolean(),
});

export type ArticleEditorValues = z.infer<typeof articleEditorSchema>;
