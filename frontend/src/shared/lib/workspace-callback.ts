import { legacyRedirect } from "@/shared/config/legacy-redirect";
import { routes } from "@/shared/config/routes";
import { sanitizeCallbackUrl } from "./sanitize-callback-url";

/** Keep existing content destinations, but finish retired checkout flows in the workspace. */
export function resolveWorkspaceCallback(raw: string | null | undefined): string {
  const safe = sanitizeCallbackUrl(raw, routes.home);
  const pathname = new URL(safe, "https://workspace.invalid").pathname;
  return legacyRedirect(pathname, true) ?? safe;
}
