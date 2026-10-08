"use client";

import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  authLoginApi,
  CONSENT_MISSING_ERROR_CODE,
  VerifyOtpError,
  type VerifyOtpRequest,
} from "./api";

export function useVerifyOtp() {
  const mutation = useMutation({
    mutationFn: (request: VerifyOtpRequest) => authLoginApi.verifyOtp(request),
    onError: (error) => {
      // Если backend вернул "нужны обязательные согласия" — UI переключится
      // на ConsentsStep, toast не нужен. Все остальные ошибки — toast как раньше.
      if (error instanceof VerifyOtpError && error.code === CONSENT_MISSING_ERROR_CODE) {
        return;
      }
      toast.error(getErrorMessage(error, "Неверный код"));
    },
  });

  return {
    verifyOtp: mutation.mutateAsync,
    isPending: mutation.isPending,
  };
}
