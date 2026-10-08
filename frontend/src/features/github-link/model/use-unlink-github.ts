"use client";

import { profileQueryOptions } from "@/entities/profile";
import { usersAdminApi } from "@/entities/user";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUnlinkGitHub() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: usersAdminApi.unlinkGitHub,
    onSuccess: async () => {
      toast.success("GitHub аккаунт отвязан");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка отвязки GitHub"));
    },
  });

  return {
    unlinkGitHub: mutation.mutate,
    isPending: mutation.isPending,
  };
}
