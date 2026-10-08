"use client";

import { useMutation } from "@tanstack/react-query";
import {
  adminCampaignsApi,
  type AdminCampaignSlug,
  type RunCampaignResponse,
} from "@/entities/admin-campaigns";
import { getErrorMessage } from "@/shared/api";
import { toast } from "sonner";

/**
 * Запуск кампании-рассылки всей её аудитории (#554 level-test, #704 email-only-login).
 * Идемпотентна per-user — повторный запуск пропускает уже-уведомлённых.
 */
export function useRunCampaign(slug: AdminCampaignSlug) {
  return useMutation({
    mutationFn: () => adminCampaignsApi.run(slug),
    onSuccess: (result: RunCampaignResponse) => {
      toast.success(`Рассылка запущена: ${result.queued} получателям`);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка запуска рассылки")),
  });
}
