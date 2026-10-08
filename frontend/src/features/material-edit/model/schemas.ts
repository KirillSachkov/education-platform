import { z } from "zod";

export const MATERIAL_KINDS = ["ARTICLE", "VIDEO", "NOTE", "STREAM"] as const;

export const MATERIAL_ACCESS_TYPES = ["PUBLIC", "REGISTERED", "ENROLLED"] as const;

const MARKDOWN_CONTENT_MAX_LENGTH = 1_000_000;

export const materialFormSchema = z.object({
  title: z.string().trim().min(1, "Введите название материала").max(200, "Максимум 200 символов"),
  // Лимиты зеркалят backend MarkdownContent.MAX_LENGTH (1_000_000).
  content: z.string().trim().max(MARKDOWN_CONTENT_MAX_LENGTH, "Максимум 1000000 символов"),
  // Авторское «Описание» (полезные ссылки / материалы для урока). Отдельно от
  // content (AI-конспект). Optional — поле необязательное для любого типа.
  description: z
    .string()
    .trim()
    .max(MARKDOWN_CONTENT_MAX_LENGTH, "Максимум 1000000 символов")
    .optional(),
  kind: z.enum(MATERIAL_KINDS),
  accessType: z.enum(MATERIAL_ACCESS_TYPES),
});

export type MaterialFormValues = z.infer<typeof materialFormSchema>;
