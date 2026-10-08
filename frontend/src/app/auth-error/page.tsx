import type { Metadata } from "next";
import { AuthErrorContent } from "./auth-error-content";
import { LogoMark } from "@/shared/ui/kit/logo";

export const metadata: Metadata = {
  title: "Ошибка авторизации",
};

type Props = {
  searchParams: Promise<{
    next?: string | string[];
  }>;
};

export default async function AuthErrorPage({ searchParams }: Props) {
  const params = await searchParams;
  const next = typeof params.next === "string" ? params.next : null;

  return (
    <div className="flex min-h-svh items-center justify-center bg-background relative overflow-hidden">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[20%] left-[20%] w-[350px] h-[350px] rounded-full bg-red/[0.04] blur-3xl" />
      </div>

      <div className="relative mx-auto flex w-full max-w-sm flex-col items-center gap-6 px-4 text-center">
        <div className="flex flex-col items-center gap-3">
          <LogoMark size={40} className="text-primary" />
          <h1 className="text-2xl font-extrabold tracking-tight font-[family-name:var(--font-sora)]">
            Sachkov<span className="text-primary">Learn</span>
          </h1>
          <p className="text-sm text-muted-foreground">
            Не удалось завершить действие
          </p>
        </div>

        <AuthErrorContent next={next} />

        <p className="text-xs text-muted-foreground/70">
          Если проблема повторяется, обратитесь к администратору платформы.
        </p>
      </div>
    </div>
  );
}
