import { z } from "zod";
import { COURSE_KINDS } from "@/shared/config/course-kind";

export const courseFormSchema = z.object({
  title: z
    .string()
    .min(1, "Название обязательно")
    .max(200, "Максимум 200 символов"),
  description: z
    .string()
    .min(1, "Описание обязательно")
    .max(2000, "Максимум 2000 символов"),
  slug: z
    .string()
    .min(2, "Минимум 2 символа")
    .max(100, "Максимум 100 символов")
    .regex(
      /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/,
      "Только строчные латинские буквы, цифры и дефисы",
    ),
  kind: z.enum(COURSE_KINDS),
  /** Display-флаг «входит в полный доступ» (issue #418) — default true. */
  showInFullAccess: z.boolean(),
});

export type CourseFormData = z.infer<typeof courseFormSchema>;
