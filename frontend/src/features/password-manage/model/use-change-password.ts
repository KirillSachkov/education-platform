"use client";

import { profileQueryOptions } from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

type ChangePasswordRequest = {
  currentPassword: string;
  newPassword: string;
};

import { AUTH_ORIGIN } from "@/shared/config";

async function changePassword(request: ChangePasswordRequest) {
  const res = await fetch(`${AUTH_ORIGIN}/auth/password/change`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(data?.error?.messages?.[0]?.message ?? "Не удалось сменить пароль");
  }
}

export function useChangePassword() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: changePassword,
    onSuccess: async () => {
      toast.success("Пароль изменён");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка смены пароля"));
    },
  });

  return {
    changePassword: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
