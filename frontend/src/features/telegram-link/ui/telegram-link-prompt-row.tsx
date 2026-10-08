"use client";

import { profileQueryOptions } from "@/entities/profile";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useTelegramLink } from "../model/use-telegram-link";

/**
 * Compact prompt rendered at the top of the notification dropdown for users who
 * haven't linked Telegram — the highest-intent surface to pitch "get these in
 * Telegram". One-click via {@link useTelegramLink}. Renders null once linked.
 *
 * Lives in features/telegram-link (so it owns useTelegramLink within-slice) and
 * is injected into the bell via NotificationBell's `topSlot` from the widget
 * layer — features/notifications must not import features/telegram-link
 * (FSD cross-slice rule).
 */
export function TelegramLinkPromptRow() {
  const { data: profile } = useQuery(profileQueryOptions.getMyProfileOptions());
  const { linkTelegram, isPending } = useTelegramLink();

  if (!profile || profile.hasTelegramLinked) {
    return null;
  }

  return (
    <button
      type="button"
      onClick={() => linkTelegram()}
      disabled={isPending}
      aria-label="Привязать Telegram"
      className="flex w-full items-center gap-3 border-b border-border bg-sky-500/[0.06] px-4 py-3 text-left transition-colors hover:bg-sky-500/[0.12] disabled:opacity-60"
    >
      <span
        aria-hidden
        className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-sky-500/15 text-sky-500"
      >
        <Icons.telegram className="size-4" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-medium leading-snug">
          Получай уведомления в Telegram
        </span>
        <span className="block text-xs text-muted-foreground">Привязать в один клик</span>
      </span>
      <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground" />
    </button>
  );
}
