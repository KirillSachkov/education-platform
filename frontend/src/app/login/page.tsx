import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { LoginForm } from "@/features/auth-login";
import { auth } from "@/shared/auth/auth";
import { sanitizeCallbackUrl } from "@/shared/lib/sanitize-callback-url";
import { LogoMark } from "@/shared/ui/kit/logo";
import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { LoginProviders } from "./providers";

export const metadata: Metadata = {
  title: "Войти",
  description: "Вход в личный кабинет SachkovLearn — образовательной платформы для разработчиков.",
  robots: { index: false, follow: false },
};

const GITHUB_ERROR_MESSAGES: Record<string, string> = {
  // GitHub-вход отключён по 149-ФЗ (#696) — старые закладки/бандлы редиректятся сюда.
  "github-login-disabled":
    "Вход через GitHub отключён — так требует закон. Введите почту вашего GitHub-аккаунта: аккаунт тот же, код придёт на неё.",
  // Legacy-коды остаются: их эмитит выживший link-flow (GitHubCallback до dispatch'а).
  github_auth_failed: "Не удалось подключить GitHub. Попробуйте ещё раз из настроек",
  github_email_required: "У GitHub аккаунта должен быть публичный email",
};

export default async function LoginPage({
  searchParams,
}: {
  searchParams: Promise<{ error?: string; callbackUrl?: string }>;
}) {
  const session = await auth();
  const { error, callbackUrl } = await searchParams;

  // A session with `error` is a dead session (refresh token invalid) — the user
  // is mid-logout. Do NOT redirect it back to the app, or SessionGuard bounces it
  // straight here again → infinite reload. Show the login form so they can re-auth.
  if (session && !session.error) {
    redirect(sanitizeCallbackUrl(callbackUrl, "/home"));
  }
  const errorMessage = error ? GITHUB_ERROR_MESSAGES[error] : null;

  return (
    <div className="flex min-h-svh bg-background relative overflow-hidden">
      {/* Background decoration */}
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[10%] left-[15%] w-[400px] h-[400px] rounded-full bg-primary/4 blur-3xl" />
        <div className="absolute bottom-[10%] right-[10%] w-[350px] h-[350px] rounded-full bg-cyan/3 blur-3xl" />
      </div>

      {/* Back link */}
      <Link
        href="/"
        className="absolute top-6 left-6 z-10 flex items-center gap-1.5 text-sm text-muted-foreground transition-colors hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        На главную
      </Link>

      {/* Center card */}
      <div className="relative m-auto flex w-full max-w-sm flex-col items-center gap-8 px-4">
        {/* Logo + brand */}
        <div className="flex flex-col items-center gap-4">
          <LogoMark size={48} className="text-primary" />
          <div className="text-center">
            <h1 className="text-2xl font-extrabold tracking-tight font-[family-name:var(--font-sora)]">
              Sachkov<span className="text-primary">Learn</span>
            </h1>
            <p className="text-sm text-muted-foreground mt-1">
              Платформа для изучения .NET разработки
            </p>
          </div>
        </div>

        {/* Login card */}
        <div className="w-full rounded-2xl border border-border/60 bg-card p-6 shadow-sm">
          <LoginProviders>
            {errorMessage && (
              <div className="rounded-lg border border-red/20 bg-red-dim px-4 py-3 mb-4">
                <p className="text-sm text-red">{errorMessage}</p>
              </div>
            )}
            <LoginForm />
          </LoginProviders>
        </div>

        <p className="text-center text-xs text-muted-foreground/70">
          Продолжая, вы соглашаетесь с условиями использования платформы.
        </p>
      </div>
    </div>
  );
}
