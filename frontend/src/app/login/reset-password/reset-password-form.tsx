"use client";

import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import {
  ArrowLeft,
  CheckCircle2,
  KeyRound,
  Loader2,
} from "lucide-react";
import { LogoMark } from "@/shared/ui/kit/logo";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { AUTH_ORIGIN, routes } from "@/shared/config";
import { translateErrorCode } from "@/shared/api/error-messages";
import { Suspense, useState } from "react";
import { toast } from "sonner";

function ResetPasswordFormInner() {
  const searchParams = useSearchParams();
  const email = searchParams.get("email") ?? "";
  const token = searchParams.get("token") ?? "";

  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [isPending, setIsPending] = useState(false);
  const [isReset, setIsReset] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (newPassword !== confirmPassword || newPassword.length < 8) return;

    setIsPending(true);
    try {
      const res = await fetch(`${AUTH_ORIGIN}/auth/password/reset`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, token, newPassword }),
      });

      if (!res.ok) {
        const fallback = "Не удалось сбросить пароль";
        const data = await res.json().catch(() => null);
        const firstError = data?.error?.messages?.[0];
        const translated = firstError?.code
          ? translateErrorCode(firstError.code, firstError.message ?? fallback)
          : (firstError?.message ?? fallback);
        toast.error(translated);
        return;
      }

      setIsReset(true);
    } catch {
      toast.error("Не удалось сбросить пароль");
    } finally {
      setIsPending(false);
    }
  };

  if (!email || !token) {
    return (
      <div className="text-center text-sm text-muted-foreground">
        <p>Недействительная ссылка для сброса пароля.</p>
        <Link href={routes.login}>
          <Button variant="ghost" size="sm" className="gap-1.5 mt-4">
            <ArrowLeft className="size-4" />
            Вернуться ко входу
          </Button>
        </Link>
      </div>
    );
  }

  if (isReset) {
    return (
      <div className="flex flex-col items-center gap-3 text-center">
        <CheckCircle2 className="size-10 text-green-500" />
        <p className="text-sm font-medium">Пароль успешно сброшен</p>
        <p className="text-xs text-muted-foreground">
          Теперь вы можете войти с новым паролем.
        </p>
        <Link href={routes.login}>
          <Button variant="ghost" size="sm" className="gap-1.5 mt-2">
            <ArrowLeft className="size-4" />
            Войти
          </Button>
        </Link>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col items-center gap-2 text-center">
        <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10">
          <KeyRound className="size-5 text-primary" />
        </div>
        <p className="text-xs text-muted-foreground">
          Введите новый пароль для аккаунта {email}
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="new-password">Новый пароль</Label>
        <Input
          id="new-password"
          type="password"
          autoComplete="new-password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          minLength={8}
          required
          autoFocus
          disabled={isPending}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="confirm-password">Подтвердите пароль</Label>
        <Input
          id="confirm-password"
          type="password"
          autoComplete="new-password"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          minLength={8}
          required
          aria-invalid={Boolean(confirmPassword) && newPassword !== confirmPassword}
          aria-describedby="confirm-password-error"
          disabled={isPending}
        />
        <p
          id="confirm-password-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {confirmPassword && newPassword !== confirmPassword ? "Пароли не совпадают" : ""}
        </p>
      </div>

      <Button
        type="submit"
        size="lg"
        className="w-full"
        disabled={
          isPending || newPassword.length < 8 || newPassword !== confirmPassword
        }
      >
        {isPending && <Loader2 className="size-4 animate-spin mr-2" />}
        Сбросить пароль
      </Button>

      <Link href={routes.login} className="mx-auto">
        <Button variant="ghost" size="sm" className="gap-1.5">
          <ArrowLeft className="size-4" />
          Назад
        </Button>
      </Link>
    </form>
  );
}

export default function ResetPasswordForm() {
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
              Сброс пароля
            </h1>
          </div>
        </div>

        <div className="w-full rounded-2xl border border-border/60 bg-card p-6 shadow-sm">
          <Suspense fallback={null}>
            <ResetPasswordFormInner />
          </Suspense>
        </div>
      </div>
    </div>
  );
}
