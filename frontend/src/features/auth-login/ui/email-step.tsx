"use client";

import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Mail } from "lucide-react";
import { useForm } from "react-hook-form";
import { emailSchema, type EmailFormValues } from "../model/schemas";

type Props = {
  onSubmit: (email: string) => void;
  isPending: boolean;
  defaultEmail?: string;
  hasPricingIntent?: boolean;
};

export function EmailStep({ onSubmit, isPending, defaultEmail, hasPricingIntent = false }: Props) {
  const form = useForm<EmailFormValues>({
    resolver: zodResolver(emailSchema),
    defaultValues: { email: defaultEmail ?? "" },
  });

  const handleSubmit = (values: EmailFormValues) => {
    onSubmit(values.email);
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-center gap-2 text-center">
        <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10">
          <Mail className="size-5 text-primary" />
        </div>
        <p className="text-sm font-medium">Вход или регистрация</p>
        <p className="text-xs text-muted-foreground">
          По коду из письма вы войдёте в аккаунт или создадите новый.
        </p>
        {hasPricingIntent ? (
          <p className="text-xs text-muted-foreground">
            После входа вернём вас к оформлению выбранного плана.
          </p>
        ) : null}
      </div>

      <form onSubmit={form.handleSubmit(handleSubmit)} className="flex flex-col gap-3">
        <div>
          <Input
            type="email"
            inputMode="email"
            // `username webauthn` — Passkey conditional UI (#305): browsers with
            // a saved passkey will surface it in the autofill dropdown without
            // any extra JS. Once passkey backend lands the on-mount hook in
            // features/passkey-authenticate will call
            // navigator.credentials.get({ mediation: "conditional" }) to wire
            // selection into the auth flow. Until then this is a no-op hint —
            // safe to ship now (browsers fall back to plain `email` semantics).
            autoComplete="username webauthn"
            placeholder="you@example.com"
            autoFocus
            required
            disabled={isPending}
            {...form.register("email")}
          />
          {form.formState.errors.email && (
            <p className="mt-1 text-sm text-destructive" role="alert" aria-live="polite">
              {form.formState.errors.email.message}
            </p>
          )}
        </div>

        <Button type="submit" size="lg" className="w-full gap-2" disabled={isPending}>
          {isPending ? <Loader2 className="size-4 animate-spin" /> : <Mail className="size-4" />}
          Продолжить
        </Button>
      </form>

      {/* Временная подсказка после отключения GitHub-входа (#696) — снять через ~2 месяца. */}
      <p className="text-center text-xs text-muted-foreground/70">
        Раньше входили через GitHub? Введите ту же почту — аккаунт сохранён.
      </p>
    </div>
  );
}
