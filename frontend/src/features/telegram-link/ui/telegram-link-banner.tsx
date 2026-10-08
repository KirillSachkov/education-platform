"use client";

import { profileQueryOptions } from "@/entities/profile";
import { useDismissibleNudge } from "@/shared/lib/dismissible-nudge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import { useTelegramLink } from "../model/use-telegram-link";

const NUDGE_STORAGE_KEY = "tg-link-nudge";

/**
 * In-flow, dismissable banner nudging registered users to link Telegram. Shown
 * on /home only when the user hasn't linked yet — brand-new visitors are spared
 * (≥2 visits) and a dismissal is remembered for 30 days. One-click: the CTA opens
 * the bot deep-link via {@link useTelegramLink} (no detour through /settings).
 *
 * In-flow (scrolls with the page) by design — it can't overlap the mobile
 * bottom-nav and it reaches desktop too, unlike the fixed-bottom PWA banner.
 */
export function TelegramLinkBanner() {
  const { data: profile } = useQuery(profileQueryOptions.getMyProfileOptions());
  const { show, dismiss } = useDismissibleNudge({
    storageKey: NUDGE_STORAGE_KEY,
    minVisits: 2,
    revealDelayMs: 600,
  });
  const { linkTelegram, isPending } = useTelegramLink();

  // Wait for the profile before deciding — avoids a flash for already-linked users.
  if (!profile || profile.hasTelegramLinked || !show) {
    return null;
  }

  return (
    <section
      role="region"
      aria-labelledby="tg-link-nudge-title"
      className="relative overflow-hidden rounded-xl border border-border/60 bg-card"
    >
      <div aria-hidden className="pointer-events-none absolute inset-0">
        <div className="absolute -right-10 -top-16 size-56 rounded-full bg-sky-500/[0.10] blur-3xl" />
      </div>

      <button
        type="button"
        onClick={dismiss}
        aria-label="Скрыть подсказку"
        className="absolute right-2 top-2 z-10 inline-flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
      >
        <Icons.close className="size-4" />
      </button>

      <div className="relative flex flex-col gap-4 p-5 pr-12 sm:flex-row sm:items-center sm:justify-between sm:gap-8 sm:p-6">
        <div className="flex min-w-0 items-start gap-3">
          <span
            aria-hidden
            className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-sky-500/15 text-sky-500"
          >
            <Icons.telegram className="size-5" />
          </span>
          <div className="min-w-0">
            <h2
              id="tg-link-nudge-title"
              className="text-balance text-base font-semibold leading-snug"
            >
              Привяжи Telegram и получи новые возможности
            </h2>
            <p className="mt-1 max-w-xl text-sm text-muted-foreground">
              Уведомления о проверке заданий и ответах автора — прямо в мессенджер, плюс доступ в
              чаты курсов.
            </p>
          </div>
        </div>

        <Button
          className="w-full shrink-0 sm:w-auto"
          onClick={() => linkTelegram()}
          disabled={isPending}
        >
          <Icons.telegram className="size-4" />
          Привязать
        </Button>
      </div>
    </section>
  );
}
