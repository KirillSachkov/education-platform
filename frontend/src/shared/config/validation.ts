import { z } from "zod";

export const PASSWORD_MIN_LENGTH = 8;

export const passwordSchema = z
  .string()
  .min(PASSWORD_MIN_LENGTH, `Минимум ${PASSWORD_MIN_LENGTH} символов`)
  .regex(/[A-Z]/, "Должен содержать заглавную букву")
  .regex(/[a-z]/, "Должен содержать строчную букву")
  .regex(/[0-9]/, "Должен содержать цифру");
