import { t } from "@/shared/i18n";

/**
 * Translates a backend error code to a localized user-facing message.
 * Looks up the code in the current locale dictionary (shared/i18n/locales/).
 * Falls back to `fallbackMessage` (typically the backend `message` field,
 * which is already in Russian after the backend localization).
 */
export function translateErrorCode(
  code: string,
  fallbackMessage: string,
): string {
  return t(code, fallbackMessage);
}
