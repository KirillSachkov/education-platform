"use client";

import {
  notificationsApi,
  type BroadcastNotificationRequest,
} from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";

export function useBroadcastNotification() {
  const mutation = useMutation({
    mutationFn: (request: BroadcastNotificationRequest) =>
      notificationsApi.broadcast(request),
    onSuccess: (response) => {
      toast.success(
        response.estimatedRecipients > 0
          ? `Рассылка запущена — получателей: ${response.estimatedRecipients}`
          : "Рассылка запущена, но подписчиков пока нет",
      );
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Ошибка отправки рассылки"));
    },
  });

  return {
    broadcast: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
