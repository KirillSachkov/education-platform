"use client";

import { usersAdminApi } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { fullLogout } from "@/shared/auth";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

export function useRevokeSessions() {
  const mutation = useMutation({
    mutationFn: usersAdminApi.revokeSessions,
    onSuccess: async () => {
      toast.success("Все сессии завершены");
      await fullLogout();
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка завершения сессий"));
    },
  });

  return {
    revokeSessions: mutation.mutate,
    isPending: mutation.isPending,
  };
}
