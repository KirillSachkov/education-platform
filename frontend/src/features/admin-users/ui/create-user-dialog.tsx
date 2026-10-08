"use client";

import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { FormDialog } from "@/shared/ui/components";
import { getErrorMessage, setServerErrors } from "@/shared/api";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { useCreateUser } from "../model/use-create-user";
import { PLATFORM_ROLES } from "@/shared/config/roles";
import {
  createUserSchema,
  type CreateUserFormData,
} from "../model/schemas";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const defaultValues: CreateUserFormData = {
  email: "",
  username: "",
  password: "",
  roles: ["platform-participant"],
};

export function CreateUserDialog({ open, onOpenChange }: Props) {
  const { createUser, isPending } = useCreateUser();

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    setError,
    formState: { errors },
  } = useForm<CreateUserFormData>({
    resolver: zodResolver(createUserSchema),
    defaultValues,
  });

  const selectedRoles = watch("roles");

  const handleClose = () => {
    reset(defaultValues);
    onOpenChange(false);
  };

  const toggleRole = (role: string) => {
    const current = selectedRoles ?? [];
    const next = current.includes(role)
      ? current.filter((r) => r !== role)
      : [...current, role];
    setValue("roles", next, { shouldValidate: true });
  };

  // Создание юзера — единственный диалог с серверной field-level валидацией
  // (USERNAME_ALREADY_TAKEN и т.п. через setServerErrors). Поэтому ждём
  // ответ и закрываем только на успех — иначе админ потеряет инфу о том,
  // какое поле зафейлилось.
  const onSubmit = async (data: CreateUserFormData) => {
    try {
      await createUser(data);
      handleClose();
    } catch (error) {
      if (!setServerErrors(error, setError)) {
        toast.error(getErrorMessage(error, "Ошибка создания пользователя"));
      }
    }
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Создание пользователя"
      description="Заполните данные нового пользователя"
      onSubmit={handleSubmit(onSubmit)}
      onCancel={handleClose}
      isPending={isPending}
      submitLabel="Создать"
    >
      <div className="space-y-2">
        <Label htmlFor="email">Email</Label>
        <Input
          id="email"
          type="email"
          inputMode="email"
          autoComplete="off"
          aria-invalid={Boolean(errors.email)}
          aria-describedby="create-user-email-error"
          {...register("email")}
          placeholder="user@example.com"
        />
        <p
          id="create-user-email-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {errors.email?.message ?? ""}
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="username">Имя пользователя</Label>
        <Input
          id="username"
          autoComplete="off"
          aria-invalid={Boolean(errors.username)}
          aria-describedby="create-user-username-error"
          {...register("username")}
          placeholder="username"
        />
        <p
          id="create-user-username-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {errors.username?.message ?? ""}
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="password">Пароль</Label>
        <Input
          id="password"
          type="password"
          autoComplete="new-password"
          aria-invalid={Boolean(errors.password)}
          aria-describedby="create-user-password-error"
          {...register("password")}
          placeholder="Минимум 8 символов"
        />
        <p
          id="create-user-password-error"
          role="alert"
          aria-live="polite"
          className="text-sm text-destructive min-h-[1.25rem]"
        >
          {errors.password?.message ?? ""}
        </p>
      </div>

      <div className="space-y-2">
        <Label>Роли</Label>
        <div className="flex flex-wrap gap-2">
          {PLATFORM_ROLES.map((role) => (
            <Button
              key={role.value}
              type="button"
              size="sm"
              variant={
                selectedRoles?.includes(role.value) ? "default" : "outline"
              }
              onClick={() => toggleRole(role.value)}
            >
              {role.label}
            </Button>
          ))}
        </div>
        {errors.roles && (
          <p className="text-sm text-destructive">{errors.roles.message}</p>
        )}
      </div>
    </FormDialog>
  );
}
