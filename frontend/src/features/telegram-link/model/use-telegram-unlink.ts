"use client";

import { profileQueryOptions } from "@/entities/profile";
import { usersAdminApi } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useTelegramUnlink() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: usersAdminApi.unlinkTelegram,
    onSuccess: async () => {
      toast.success("Telegram отвязан");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка отвязки Telegram"));
    },
  });

  return {
    unlinkTelegram: mutation.mutate,
    isPending: mutation.isPending,
  };
}
