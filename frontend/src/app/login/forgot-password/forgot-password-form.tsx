"use client";

import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Loader2, Mail, ArrowLeft, CheckCircle2 } from "lucide-react";
import { LogoMark } from "@/shared/ui/kit/logo";
import Link from "next/link";
import { AUTH_ORIGIN, routes } from "@/shared/config";
import { useState } from "react";

export default function ForgotPasswordForm() {
  const [email, setEmail] = useState("");
  const [isPending, setIsPending] = useState(false);
  const [isSent, setIsSent] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsPending(true);
    try {
      await fetch(`${AUTH_ORIGIN}/auth/password/forgot`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });
      setIsSent(true);
    } finally {
      setIsPending(false);
    }
  };

  return (
    <div className="flex min-h-svh bg-background relative overflow-hidden">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[10%] left-[15%] w-[400px] h-[400px] rounded-full bg-primary/4 blur-3xl" />
        <div className="absolute bottom-[10%] right-[10%] w-[350px] h-[350px] rounded-full bg-cyan/3 blur-3xl" />
      </div>

      <div className="relative m-auto flex w-full max-w-sm flex-col items-center gap-8 px-4">
        <div className="flex flex-col items-center gap-4">
          <LogoMark size={48} className="text-primary" />
          <div className="text-center">
            <h1 className="text-2xl font-extrabold tracking-tight">
              Восстановление пароля
            </h1>
          </div>
        </div>

        <div className="w-full rounded-2xl border border-border/60 bg-card p-6 shadow-sm">
          {isSent ? (
            <div className="flex flex-col items-center gap-3 text-center">
              <CheckCircle2 className="size-10 text-green-500" />
              <p className="text-sm font-medium">Письмо отправлено</p>
              <p className="text-xs text-muted-foreground">
                Если аккаунт с таким email существует, мы отправили ссылку для
                сброса пароля. Проверьте почту.
              </p>
              <Link href={routes.login}>
                <Button variant="ghost" size="sm" className="gap-1.5 mt-2">
                  <ArrowLeft className="size-4" />
                  Вернуться ко входу
                </Button>
              </Link>
            </div>
          ) : (
            <form onSubmit={handleSubmit} className="flex flex-col gap-4">
              <div className="flex flex-col items-center gap-2 text-center">
                <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10">
                  <Mail className="size-5 text-primary" />
                </div>
                <p className="text-xs text-muted-foreground">
                  Введите email, привязанный к вашему аккаунту
                </p>
              </div>

              <Input
                type="email"
                inputMode="email"
                autoComplete="email"
                placeholder="you@example.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoFocus
                disabled={isPending}
              />

              <Button
                type="submit"
                size="lg"
                className="w-full"
                disabled={isPending || !email}
              >
                {isPending && <Loader2 className="size-4 animate-spin mr-2" />}
                Отправить ссылку
              </Button>

              <Link href={routes.login} className="mx-auto">
                <Button variant="ghost" size="sm" className="gap-1.5">
                  <ArrowLeft className="size-4" />
                  Назад
                </Button>
              </Link>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
