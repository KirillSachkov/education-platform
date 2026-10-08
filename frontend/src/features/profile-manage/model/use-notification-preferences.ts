"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  notificationQueryOptions,
  notificationsApi,
  type NotificationPreference,
  type UpdatePreferencesRequest,
} from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";

export function useNotificationPreferences() {
  const query = useQuery(notificationQueryOptions.preferences());

  return {
    preferences: query.data as NotificationPreference | undefined,
    isLoading: query.isLoading,
    error: query.error ?? null,
  };
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient();
  const mutation = useMutation({
    mutationFn: (request: UpdatePreferencesRequest) => notificationsApi.updatePreferences(request),
    onSuccess: async () => {
      toast.success("Настройки уведомлений сохранены");
      await queryClient.invalidateQueries({
        queryKey: notificationQueryOptions.preferencesKey(),
      });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка сохранения настроек уведомлений"));
    },
  });

  return {
    updatePreferences: mutation.mutate,
    isPending: mutation.isPending,
  };
}
