import { z } from "zod";
import { ACCESS_TYPES } from "@/shared/config/access-type";

export const lessonSchema = z.object({
  title: z
    .string()
    .min(1, "Название обязательно")
    .max(200, "Максимум 200 символов"),
  content: z.string().max(50000, "Максимум 50 000 символов").optional(),
  accessType: z.enum(ACCESS_TYPES),
});

export type LessonFormData = z.infer<typeof lessonSchema>;

export const lessonDefaultValues: LessonFormData = {
  title: "",
  content: "",
  accessType: "ENROLLED",
};

export const issueSchema = z.object({
  title: z
    .string()
    .min(1, "Название обязательно")
    .max(200, "Максимум 200 символов"),
  content: z.string().optional(),
  accessType: z.enum(ACCESS_TYPES),
  submissionMode: z.enum(["PULL_REQUEST", "SELF_CHECK"]),
  selfCheckInstructions: z.string().max(50000, "Максимум 50 000 символов").optional(),
});

export type IssueFormData = z.infer<typeof issueSchema>;

export const issueDefaultValues: IssueFormData = {
  title: "",
  content: "",
  accessType: "ENROLLED",
  submissionMode: "PULL_REQUEST",
  selfCheckInstructions: "",
};

export const moduleSchema = z.object({
  title: z
    .string()
    .min(1, "Название обязательно")
    .max(100, "Максимум 100 символов"),
  description: z.string().max(2000, "Максимум 2000 символов").optional(),
  detailedDescription: z.string().max(50000, "Максимум 50 000 символов").optional(),
});

export type ModuleFormData = z.infer<typeof moduleSchema>;
