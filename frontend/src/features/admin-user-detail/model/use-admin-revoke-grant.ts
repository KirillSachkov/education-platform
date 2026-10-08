"use client";

import { adminCrossServiceApi, adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Admin manual-revoke плана у юзера (#414). После отзыва инвалидируем grants-таб
 * этого юзера — статус grant'а перейдёт в REVOKED и кнопка отзыва пропадёт.
 */
export function useAdminRevokeGrant(userId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ grantId, reason }: { grantId: string; reason: string }) =>
      adminCrossServiceApi.revokeGrant(grantId, reason),
    onSuccess: async () => {
      toast.success("План отозван");
      await qc.invalidateQueries({
        queryKey: adminCrossServiceQueryOptions.getGrantsOptions(userId).queryKey,
      });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось отозвать план")),
  });
}
