"use client";

import { adminCrossServiceApi, adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Admin trial-credit-override (#580/#604) — legacy отметка для месячного доступа.
 * Paid trial credit больше не сгорает; после успеха инвалидируем grants- и
 * post-purchase-status-табы юзера, чтобы сводка отразила обновлённое состояние гранта.
 */
export function useTrialCreditOverride(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ planId, until }: { planId: string; until?: string | null }) =>
      adminCrossServiceApi.trialCreditOverride(userId, planId, until),
    onSuccess: async () => {
      toast.success("Зачёт отмечен");
      await Promise.all([
        qc.invalidateQueries({
          queryKey: adminCrossServiceQueryOptions.getGrantsOptions(userId).queryKey,
        }),
        qc.invalidateQueries({
          queryKey: adminCrossServiceQueryOptions.getPostPurchaseStatusOptions(userId).queryKey,
        }),
      ]);
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось отметить зачёт")),
  });
}
