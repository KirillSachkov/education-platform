"use client";

import {
  trainerProApi,
  trainerProAdminOfferKey,
  type UpdateTrainerProOfferRequest,
} from "@/entities/trainer-pro";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Обновляет оффер-вариант подписки «Тренажёр Pro» (#674) через trainer-API
 * (`PATCH /access/admin/trainer-pro/offer/{planId}/`) — цена, название, преимущества,
 * покупаемость (`isActive` → publish/unpublish). Интервал автопродления immutable после
 * создания (его нет в `UpdateTrainerProOfferRequest`).
 */
export function useUpdateTrainerSubscription(planId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: UpdateTrainerProOfferRequest) => trainerProApi.updateOffer(planId, req),
    onSuccess: async () => {
      toast.success("Подписка обновлена");
      await qc.invalidateQueries({ queryKey: trainerProAdminOfferKey });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка обновления подписки")),
  });
}
