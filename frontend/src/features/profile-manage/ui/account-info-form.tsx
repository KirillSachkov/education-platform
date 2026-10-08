"use client";

import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Save } from "lucide-react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useUpdateAccountInfo } from "../model/use-update-account-info";

const accountInfoSchema = z.object({
  username: z
    .string()
    .min(3, "Минимум 3 символа")
    .max(30, "Максимум 30 символов")
    .regex(/^[a-zA-Z0-9_\-.]+$/, "Допустимы латиница, цифры, _, -, ."),
  displayName: z
    .string()
    .max(50, "Максимум 50 символов")
    .optional()
    .or(z.literal("")),
});

type AccountInfoFormValues = z.infer<typeof accountInfoSchema>;

type Props = {
  username: string;
  displayName: string;
};

export function AccountInfoForm({ username, displayName }: Props) {
  const { updateAccountInfo, isPending } = useUpdateAccountInfo();

  const form = useForm<AccountInfoFormValues>({
    resolver: zodResolver(accountInfoSchema),
    values: {
      username,
      displayName,
    },
  });

  const onSubmit = (values: AccountInfoFormValues) => {
    const hasChanges =
      values.username !== username || values.displayName !== displayName;
    if (!hasChanges) return;

    updateAccountInfo({
      username: values.username !== username ? values.username : undefined,
      displayName:
        values.displayName !== displayName ? values.displayName : undefined,
    });
  };

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
      <div className="space-y-2">
        <Label htmlFor="account-username">Имя пользователя</Label>
        <Input
          id="account-username"
          placeholder="username"
          autoComplete="username"
          maxLength={30}
          aria-invalid={Boolean(form.formState.errors.username)}
          aria-describedby="account-username-error"
          {...form.register("username")}
        />
        <p
          id="account-username-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {form.formState.errors.username?.message ?? ""}
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="account-display-name">Отображаемое имя</Label>
        <Input
          id="account-display-name"
          placeholder="Имя Фамилия"
          autoComplete="name"
          maxLength={50}
          aria-invalid={Boolean(form.formState.errors.displayName)}
          aria-describedby="account-display-name-error"
          {...form.register("displayName")}
        />
        <p
          id="account-display-name-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {form.formState.errors.displayName?.message ?? ""}
        </p>
      </div>

      <Button
        type="submit"
        disabled={isPending || !form.formState.isDirty}
      >
        {isPending ? (
          <Loader2 size={16} className="mr-2 animate-spin" />
        ) : (
          <Save size={16} className="mr-2" />
        )}
        {isPending ? "Сохранение..." : "Сохранить"}
      </Button>
    </form>
  );
}
