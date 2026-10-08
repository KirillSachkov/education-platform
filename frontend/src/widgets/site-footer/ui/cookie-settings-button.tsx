"use client";

import { useCookieConsent } from "@/shared/lib/use-cookie-consent";

export function CookieSettingsButton() {
  const { reset } = useCookieConsent();
  return (
    <button
      type="button"
      onClick={reset}
      className="text-muted-foreground/70 hover:text-foreground underline transition-colors"
    >
      Настройки cookies
    </button>
  );
}
