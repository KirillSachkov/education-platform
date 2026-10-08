import type { Metadata } from "next";
import ResetPasswordForm from "./reset-password-form";

export const metadata: Metadata = {
  title: "Новый пароль",
};

export default function ResetPasswordPage() {
  return <ResetPasswordForm />;
}
