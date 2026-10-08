"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2 } from "lucide-react";
import { useForm } from "react-hook-form";
import { useSetPassword } from "../model/use-set-password";
import { setPasswordSchema, type SetPasswordFormData } from "../model/schemas";

type Props = {
  userId: string;
  userEmail: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function SetPasswordDialog({
  userId,
  userEmail,
  open,
  onOpenChange,
}: Props) {
  const { setPassword, isPending } = useSetPassword();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<SetPasswordFormData>({
    resolver: zodResolver(setPasswordSchema),
    defaultValues: { newPassword: "" },
  });

  const onSubmit = async (data: SetPasswordFormData) => {
    await setPassword({ userId, request: data });
    reset();
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>Смена пароля</DialogTitle>
          <DialogDescription>{userEmail}</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="newPassword">Новый пароль</Label>
              <Input
                id="newPassword"
                type="password"
                autoComplete="new-password"
                aria-invalid={Boolean(errors.newPassword)}
                aria-describedby="set-password-error"
                {...register("newPassword")}
                placeholder="Минимум 6 символов"
              />
              <p
                id="set-password-error"
                role="alert"
                aria-live="polite"
                className="text-sm text-destructive min-h-[1.25rem]"
              >
                {errors.newPassword?.message ?? ""}
              </p>
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isPending}
            >
              Отмена
            </Button>
            <Button type="submit" disabled={isPending}>
              {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Сменить пароль
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
