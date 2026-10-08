"use client";

import {
  profileApi,
  profileQueryOptions,
  type UpdateMyAccountInfoRequest,
} from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateAccountInfo() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateMyAccountInfoRequest) => profileApi.updateMyAccountInfo(request),
    onSuccess: async () => {
      toast.success("Данные аккаунта обновлены");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления данных аккаунта"));
    },
  });

  return {
    updateAccountInfo: mutation.mutate,
    isPending: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
