import { useMutation } from "@tanstack/react-query";
import { connectReviewAppApi, type StartInstallationRequest } from "../api";

/**
 * Стартует install-flow GitHub App для AI-проверки PR'ов (ARS).
 * При успехе хук НЕ навигирует автоматически — caller вызывает
 * `window.location.assign(data.redirectUrl)` (full-page redirect на GitHub).
 */
export function useStartReviewAppInstallation() {
  return useMutation({
    mutationFn: (request: StartInstallationRequest = {}) =>
      connectReviewAppApi.startInstallation(request),
  });
}
