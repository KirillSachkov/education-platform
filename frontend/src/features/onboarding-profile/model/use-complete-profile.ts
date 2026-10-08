"use client";

import { profileApi } from "@/entities/profile";
import { getErrorMessage } from "@/shared/api";
import { useMutation } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import { toast } from "sonner";

type Options = {
  /**
   * Куда уходим после успешного сохранения. По умолчанию — hard-redirect на `/`
   * (legacy standalone page `/onboarding/profile`). В режиме модалки передаём `null` —
   * после `update()` сессия сама подтянет новый `display_name`, и гейт схлопнется.
   */
  redirectTo?: string | null;
};

export function useCompleteProfile({ redirectTo = "/" }: Options = {}) {
  const { update } = useSession();
  return useMutation({
    mutationFn: (displayName: string) => profileApi.completeProfile({ displayName }),
    onSuccess: async () => {
      // Force JWT refresh via refresh_token grant — picks up the new display_name claim from userinfo.
      // ВАЖНО: `update()` без аргумента в next-auth v5 = GET /api/auth/session (просто
      // re-read из cookie, БЕЗ запуска jwt-callback с trigger="update"). Чтобы попасть в
      // ветку refreshAccessToken и реально пере-выдать токен — нужно передать любое
      // не-undefined значение → клиент шлёт POST с body, сервер распознаёт update-trigger.
      // signIn("auth-service") сделать нельзя: cookie auth на /connect/authorize отдаёт 401
      // (OnRedirectToLogin override) → бесконечный редирект.
      await update({ trigger: "completeProfile" });
      if (redirectTo !== null) {
        window.location.href = redirectTo;
      }
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось сохранить имя"));
    },
  });
}
