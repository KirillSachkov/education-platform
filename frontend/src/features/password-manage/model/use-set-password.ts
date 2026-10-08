"use client";

import { profileQueryOptions } from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

type SetPasswordRequest = {
  password: string;
};

import { AUTH_ORIGIN } from "@/shared/config";

async function setPassword(request: SetPasswordRequest) {
  const res = await fetch(`${AUTH_ORIGIN}/auth/password/set`, {
    method: "POST",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  });

  if (!res.ok) {
    const data = await res.json().catch(() => null);
    throw new Error(data?.error?.messages?.[0]?.message ?? "Не удалось установить пароль");
  }
}

export function useSetPassword() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: setPassword,
    onSuccess: async () => {
      toast.success("Пароль установлен");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка установки пароля"));
    },
  });

  return {
    setPassword: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
