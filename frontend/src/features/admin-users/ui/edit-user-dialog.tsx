"use client";

import type { AdminUserSummary } from "@/entities/user";
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
import { useForm } from "react-hook-form";
import { useUpdateUser } from "../model/use-update-user";
import { editUserSchema, type EditUserFormData } from "../model/schemas";

type Props = {
  user: AdminUserSummary;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function EditUserDialog({ user, open, onOpenChange }: Props) {
  const { updateUser } = useUpdateUser();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<EditUserFormData>({
    resolver: zodResolver(editUserSchema),
    defaultValues: {
      username: user.userName ?? "",
      email: user.email ?? "",
      emailConfirmed: user.emailConfirmed,
    },
  });

  // Fire-and-forget: закрываем форму сразу, тосты из useUpdateUser.
  const onSubmit = (data: EditUserFormData) => {
    void updateUser({
      userId: user.id,
      request: {
        username: data.username || undefined,
        email: data.email || undefined,
        emailConfirmed: data.emailConfirmed,
      },
    });
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[500px]">
        <DialogHeader>
          <DialogTitle>Редактирование пользователя</DialogTitle>
          <DialogDescription>{user.email}</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-4 py-4">
            <div className="space-y-2">
              <Label htmlFor="edit-username">Имя пользователя</Label>
              <Input
                id="edit-username"
                autoComplete="off"
                aria-invalid={Boolean(errors.username)}
                aria-describedby="edit-user-username-error"
                {...register("username")}
              />
              <p
                id="edit-user-username-error"
                role="alert"
                aria-live="polite"
                className="text-sm text-destructive min-h-[1.25rem]"
              >
                {errors.username?.message ?? ""}
              </p>
            </div>

            <div className="space-y-2">
              <Label htmlFor="edit-email">Email</Label>
              <Input
                id="edit-email"
                type="email"
                inputMode="email"
                autoComplete="off"
                aria-invalid={Boolean(errors.email)}
                aria-describedby="edit-user-email-error"
                {...register("email")}
              />
              <p
                id="edit-user-email-error"
                role="alert"
                aria-live="polite"
                className="text-sm text-destructive min-h-[1.25rem]"
              >
                {errors.email?.message ?? ""}
              </p>
            </div>

            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                id="edit-emailConfirmed"
                {...register("emailConfirmed")}
                className="h-4 w-4 rounded border-border"
              />
              <Label htmlFor="edit-emailConfirmed">Email подтверждён</Label>
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Отмена
            </Button>
            <Button type="submit">Сохранить</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
