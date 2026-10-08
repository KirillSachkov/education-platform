import { auth } from "@/shared/auth/auth";
import { routes } from "@/shared/config/routes";
import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { ConnectionsView } from "./connections-view";

export const metadata: Metadata = {
  title: "Свяжите аккаунты",
};

/**
 * Онбординг новых пользователей после OTP-регистрации (#696 §6).
 * Редирект сюда делает login-флоу (`/onboarding/connections?next=<callbackUrl>`);
 * страница только читает `next` и валидирует его как внутренний путь.
 */
export default async function OnboardingConnectionsPage({
  searchParams,
}: {
  searchParams: Promise<{ next?: string }>;
}) {
  const session = await auth();

  if (!session) {
    redirect(routes.login);
  }

  const { next } = await searchParams;

  return <ConnectionsView next={next ?? null} />;
}
