"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { notificationQueryOptions, notificationsApi } from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";

export function useMarkAsRead() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: (id: string) => notificationsApi.markAsRead(id),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: [notificationQueryOptions.baseKey, "list"],
        }),
        queryClient.invalidateQueries({
          queryKey: notificationQueryOptions.unreadCountKey(),
        }),
      ]);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отметить уведомление"));
    },
  });

  return {
    markAsRead: mutation.mutate,
    markAsReadAsync: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
