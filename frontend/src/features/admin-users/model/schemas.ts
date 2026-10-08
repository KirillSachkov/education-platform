import { z } from "zod";
import { passwordSchema } from "@/shared/config/validation";

export const createUserSchema = z.object({
  email: z.string().min(1, "Email обязателен").email("Некорректный email"),
  username: z
    .string()
    .min(3, "Минимум 3 символа")
    .max(30, "Максимум 30 символов")
    .regex(/^[a-zA-Z0-9_\-.]+$/, "Только латиница, цифры, _, -, ."),
  password: passwordSchema,
  roles: z.array(z.string()).min(1, "Выберите хотя бы одну роль"),
});

export type CreateUserFormData = z.infer<typeof createUserSchema>;

export const editUserSchema = z.object({
  username: z
    .string()
    .min(3, "Минимум 3 символа")
    .max(30, "Максимум 30 символов")
    .regex(/^[a-zA-Z0-9_\-.]+$/, "Только латиница, цифры, _, -, .")
    .or(z.literal(""))
    .optional(),
  email: z
    .string()
    .email("Некорректный email")
    .or(z.literal(""))
    .optional(),
  emailConfirmed: z.boolean().optional(),
});

export type EditUserFormData = z.infer<typeof editUserSchema>;

export const setPasswordSchema = z.object({
  newPassword: passwordSchema,
});

export type SetPasswordFormData = z.infer<typeof setPasswordSchema>;

