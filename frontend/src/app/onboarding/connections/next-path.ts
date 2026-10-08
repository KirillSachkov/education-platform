import { routes } from "@/shared/config/routes";
import { sanitizeCallbackUrl } from "@/shared/lib/sanitize-callback-url";

/**
 * Валидирует `?next=` как ВНУТРЕННИЙ путь платформы — защита от open redirect.
 * Единственный источник правил — {@link sanitizeCallbackUrl} (shared), здесь
 * только fallback на {@link routes.home}.
 */
export function resolveNextPath(raw: string | null | undefined): string {
  return sanitizeCallbackUrl(raw, routes.home);
}
