"use client";

import { useState } from "react";
import { useInstallPrompt } from "@/shared/lib/pwa";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";

/**
 * Renders a button that triggers the browser's native PWA install dialog.
 * Only mounts when `beforeinstallprompt` has fired — i.e. the browser
 * decided the app is installable and the user hasn't already installed it.
 *
 * Designed to sit inside the mobile profile hub (`/profile`), next to other
 * account actions. Self-hides after the user accepts the prompt or after one
 * dismissal cycle.
 *
 * iOS Safari is a known no-op (no `beforeinstallprompt` support); on that
 * platform users add via the share-sheet manually. We surface nothing rather
 * than show a button that does nothing.
 */
export function InstallPromptButton({ className }: { className?: string }) {
  const { available, install } = useInstallPrompt();
  const [dismissed, setDismissed] = useState(false);

  if (!available || !install || dismissed) return null;

  const handleClick = async () => {
    const outcome = await install();
    if (outcome === "dismissed") {
      // The browser fires the prompt once per engagement window; don't
      // re-show our CTA inside the same session if the user backed out.
      setDismissed(true);
    }
  };

  return (
    <Button
      variant="outline"
      className={className ?? "w-full justify-start gap-3 rounded-2xl h-12 px-4"}
      onClick={() => {
        void handleClick();
      }}
    >
      <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
        <Icons.download className="size-[18px]" />
      </span>
      <span className="flex-1 text-left text-sm font-medium">Установить приложение</span>
      <Icons.chevronRight className="size-4 text-muted-foreground/60" />
    </Button>
  );
}
