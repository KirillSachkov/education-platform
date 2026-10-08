"use client";

import {
  type AddHomePinRequest,
  type ReorderHomePinRequest,
  homePinsApi,
  homePinsKeys,
} from "@/entities/plan-pinned-material";
import { getErrorMessage } from "@/shared/api";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

export function useAddHomePin(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: AddHomePinRequest) => homePinsApi.add(planId, request),
    onSuccess: async () => {
      toast.success("Материал закреплён");
      await queryClient.invalidateQueries({ queryKey: homePinsKeys.forPlan(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось закрепить материал")),
  });
}

export function useUpdateHomePinNote(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ pinId, note }: { pinId: string; note: string | null }) =>
      homePinsApi.updateNote(planId, pinId, { note }),
    onSuccess: async () => {
      toast.success("Заметка обновлена");
      await queryClient.invalidateQueries({ queryKey: homePinsKeys.forPlan(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось обновить заметку")),
  });
}

export function useReorderHomePin(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ pinId, request }: { pinId: string; request: ReorderHomePinRequest }) =>
      homePinsApi.reorder(planId, pinId, request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: homePinsKeys.forPlan(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось переупорядочить закреп")),
  });
}

export function useDeleteHomePin(planId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (pinId: string) => homePinsApi.remove(planId, pinId),
    onSuccess: async () => {
      toast.success("Закреп удалён");
      await queryClient.invalidateQueries({ queryKey: homePinsKeys.forPlan(planId) });
    },
    onError: (error) => toast.error(getErrorMessage(error, "Не удалось удалить закреп")),
  });
}
