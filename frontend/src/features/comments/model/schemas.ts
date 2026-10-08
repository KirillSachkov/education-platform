import { z } from "zod";

export const commentFormSchema = z.object({
  content: z
    .string()
    .min(1, "Комментарий не может быть пустым")
    .max(1000, "Максимум 1000 символов"),
});

export type CommentFormValues = z.infer<typeof commentFormSchema>;
