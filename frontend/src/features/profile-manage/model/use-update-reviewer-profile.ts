"use client";

import {
  profileApi,
  profileQueryOptions,
  type UpdateMyReviewerProfileRequest,
} from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateReviewerProfile() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateMyReviewerProfileRequest) =>
      profileApi.updateMyReviewerProfile(request),
    onSuccess: async () => {
      toast.success("Профиль проверяющего обновлён");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления профиля проверяющего"));
    },
  });

  return {
    updateReviewerProfile: mutation.mutate,
    isPending: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
