"use client";

import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import { authLoginApi, type SendOtpRequest } from "./api";

export function useSendOtp() {
  const mutation = useMutation({
    mutationFn: (request: SendOtpRequest) => authLoginApi.sendOtp(request),
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отправить код"));
    },
  });

  return {
    sendOtp: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
