"use client";

import {
  profileApi,
  profileQueryOptions,
  type UpdateMyBaseProfileRequest,
} from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateBaseProfile() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateMyBaseProfileRequest) => profileApi.updateMyBaseProfile(request),
    onSuccess: async () => {
      toast.success("Профиль обновлён");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления профиля"));
    },
  });

  return {
    updateBaseProfile: mutation.mutate,
    isPending: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
