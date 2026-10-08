"use client";

import Link from "next/link";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { routes } from "@/shared/config/routes";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useCompleteProfile } from "../model/use-complete-profile";

const displayNameSchema = z.object({
  displayName: z.string().trim().min(1, "Введите отображаемое имя").max(50, "Максимум 50 символов"),
});

type DisplayNameFormValues = z.infer<typeof displayNameSchema>;

type Props = {
  username: string;
  initialDisplayName?: string;
  /**
   * Куда уходить после save. По умолчанию hard-redirect на `/` (standalone-страница).
   * `null` — режим модалки: после успеха ничего не редиректим, доверяем session.update()
   * подтянуть новый display_name и схлопнуть гейт.
   */
  redirectTo?: string | null;
};

export function ProfileOnboardingForm({
  username,
  initialDisplayName = "",
  redirectTo = "/",
}: Props) {
  const form = useForm<DisplayNameFormValues>({
    resolver: zodResolver(displayNameSchema),
    defaultValues: { displayName: initialDisplayName },
    mode: "onChange",
  });

  const completeMutation = useCompleteProfile({ redirectTo });
  const isPending = completeMutation.isPending;

  const onSubmit = (values: DisplayNameFormValues) => {
    if (isPending) return;
    completeMutation.mutate(values.displayName.trim());
  };

  const canSubmit = form.formState.isValid && !isPending;

  return (
    <div className="space-y-6">
      <div className="space-y-2 text-center">
        <h1 className="text-2xl font-extrabold tracking-tight">Как вас называть?</h1>
        <p className="text-sm text-muted-foreground">
          Это имя будут видеть другие участники в комментариях, на лидерборде и в профиле.
        </p>
      </div>

      <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4">
        <div className="space-y-2">
          <Label htmlFor="onboarding-display-name">Отображаемое имя</Label>
          <Input
            id="onboarding-display-name"
            placeholder="Например, Иван Иванов"
            maxLength={50}
            autoFocus
            autoComplete="name"
            aria-invalid={Boolean(form.formState.errors.displayName)}
            aria-describedby="onboarding-display-name-error"
            {...form.register("displayName")}
          />
          <p
            id="onboarding-display-name-error"
            role="alert"
            aria-live="polite"
            className="text-sm text-destructive min-h-[1.25rem]"
          >
            {form.formState.errors.displayName?.message ?? ""}
          </p>
        </div>

        <div className="rounded-lg border border-border/60 bg-muted/40 px-3 py-2.5">
          <p className="text-xs text-muted-foreground">
            Ваш юзернейм — <span className="font-mono font-medium text-foreground">@{username}</span>
            . Его можно изменить в настройках профиля.
          </p>
        </div>

        <Button type="submit" disabled={!canSubmit} className="w-full">
          {isPending ? <Icons.loading size={16} className="mr-2 animate-spin" /> : null}
          Продолжить
        </Button>

        {redirectTo !== null && (
          <Link
            href={routes.home}
            className="block text-center text-xs text-muted-foreground hover:text-foreground transition-colors"
          >
            Пропустить — заполню позже
          </Link>
        )}
      </form>
    </div>
  );
}
