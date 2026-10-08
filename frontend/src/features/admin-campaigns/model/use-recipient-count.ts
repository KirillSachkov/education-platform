"use client";

import { useQuery } from "@tanstack/react-query";
import { adminCampaignsQueryOptions, type AdminCampaignSlug } from "@/entities/admin-campaigns";

/**
 * Размер адресуемой аудитории кампании (#554 level-test, #704 email-only-login).
 * Read-only — мутации (test/run) аудиторию не меняют, поэтому не инвалидируют этот query.
 */
export function useRecipientCount(slug: AdminCampaignSlug) {
  return useQuery(adminCampaignsQueryOptions.recipientCount(slug));
}
