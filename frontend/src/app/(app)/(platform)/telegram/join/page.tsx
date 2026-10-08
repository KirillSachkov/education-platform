import type { Metadata } from "next";
import { TelegramJoinPageClient } from "./page-client";

export const metadata: Metadata = {
  title: "Вступи в Telegram-группу",
};

interface Props {
  /** `?plan=<planId>` — план, чьи Telegram-группы показываем. */
  searchParams: Promise<{ plan?: string }>;
}

/**
 * `/telegram/join` — standalone-страница вступления в Telegram-группу плана.
 * Цель приземления для out-of-band уведомлений/писем (`TelegramJoinReminder`):
 * проводит юзера через привязку Telegram + вступление в группу, потому что бот
 * отклоняет join-request'ы от непривязанных к платформе аккаунтов.
 */
export default async function TelegramJoinPage({ searchParams }: Props) {
  const { plan } = await searchParams;
  return <TelegramJoinPageClient planId={plan ?? null} />;
}
