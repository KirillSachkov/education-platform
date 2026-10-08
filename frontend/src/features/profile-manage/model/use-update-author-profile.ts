"use client";

import {
  profileApi,
  profileQueryOptions,
  type UpdateMyAuthorProfileRequest,
} from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useUpdateAuthorProfile() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (request: UpdateMyAuthorProfileRequest) =>
      profileApi.updateMyAuthorProfile(request),
    onSuccess: async () => {
      toast.success("Профиль автора обновлён");
      await queryClient.invalidateQueries({ queryKey: profileQueryOptions.getMyProfileKey() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка обновления профиля автора"));
    },
  });

  return {
    updateAuthorProfile: mutation.mutate,
    isPending: mutation.isPending,
    isError: mutation.isError,
    error: mutation.error,
  };
}
