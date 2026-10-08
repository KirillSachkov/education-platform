"use client";

import { adminCrossServiceApi } from "@/entities/admin-cross-service";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { invalidateAccessCommunities } from "./invalidate";

/**
 * Admin support-action (#444): переотправить Telegram invite-ссылки юзеру по всем
 * его активным грантам (admin-вариант user-facing «обновить приглашения»).
 */
export function useResendTelegramInvites(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => adminCrossServiceApi.resyncTelegramInvites(userId),
    onSuccess: async (data) => {
      const result = data.result;
      if (!result?.telegramLinked) {
        toast.warning("Пользователь не привязал Telegram");
      } else if (result.invitesSent > 0) {
        toast.success(`Отправлено приглашений: ${result.invitesSent}`);
      } else {
        toast.info("Нет чатов для приглашения");
      }
      await invalidateAccessCommunities(qc, userId);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось обновить приглашения")),
  });
}
