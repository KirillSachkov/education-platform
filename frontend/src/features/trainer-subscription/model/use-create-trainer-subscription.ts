"use client";

import {
  trainerProApi,
  trainerProAdminOfferKey,
  type CreateTrainerProOfferRequest,
} from "@/entities/trainer-pro";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

/**
 * Создаёт оффер-вариант подписки «Тренажёр Pro» (#674) через выделенный trainer-API
 * (`POST /access/admin/trainer-pro/offer/`). Бэкенд сам выставляет tier=SUBSCRIPTION +
 * capability + offerType TRAINER_PRO + Scope=TRAINER. При `isActive=true` (default) вариант
 * сразу публикуется (становится покупаемым).
 */
export function useCreateTrainerSubscription() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateTrainerProOfferRequest) => trainerProApi.createOffer(req),
    onSuccess: async () => {
      toast.success("Подписка создана");
      await qc.invalidateQueries({ queryKey: trainerProAdminOfferKey });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Ошибка создания подписки")),
  });
}
