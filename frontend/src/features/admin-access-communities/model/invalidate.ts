import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import type { QueryClient } from "@tanstack/react-query";

/**
 * Все три support-action'а (#444) меняют post-purchase-сводку (telegram.status /
 * onboarding / invites) и Telegram-привязку. Инвалидируем оба query-key'я,
 * чтобы вкладка «Доступы и сообщества» перечиталась после действия.
 */
export async function invalidateAccessCommunities(
  qc: QueryClient,
  userId: string,
): Promise<void> {
  await Promise.all([
    qc.invalidateQueries({
      queryKey: adminCrossServiceQueryOptions.getPostPurchaseStatusOptions(userId).queryKey,
    }),
    qc.invalidateQueries({
      queryKey: adminCrossServiceQueryOptions.getUserTelegramLinkOptions(userId).queryKey,
    }),
  ]);
}
