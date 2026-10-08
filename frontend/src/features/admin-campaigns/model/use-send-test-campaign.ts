"use client";

import { useMutation } from "@tanstack/react-query";
import { adminCampaignsApi, type AdminCampaignSlug } from "@/entities/admin-campaigns";
import { getErrorMessage } from "@/shared/api";
import { toast } from "sonner";

/**
 * Тестовая отправка одного уведомления кампании самому админу (#554, #704).
 * Non-campaign correlation — отправку можно повторять, реальную кампанию не трогает.
 * `successMessage` зависит от каналов кампании (письмо на почту vs in-app инбокс).
 */
export function useSendTestCampaign(slug: AdminCampaignSlug, successMessage: string) {
  return useMutation({
    mutationFn: () => adminCampaignsApi.sendTest(slug),
    onSuccess: () => {
      toast.success(successMessage);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка тестовой отправки")),
  });
}
