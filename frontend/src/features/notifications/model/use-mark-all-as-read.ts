"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { notificationQueryOptions, notificationsApi } from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";

export function useMarkAllAsRead() {
  const queryClient = useQueryClient();

  const mutation = useMutation({
    mutationFn: () => notificationsApi.markAllAsRead(),
    onSuccess: async (data) => {
      if (data.updated > 0) {
        toast.success(`Прочитано ${data.updated}`);
      }
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
      toast.error(getErrorMessage(error, "Не удалось отметить все уведомления"));
    },
  });

  return {
    markAllAsRead: mutation.mutate,
    isPending: mutation.isPending,
  };
}
