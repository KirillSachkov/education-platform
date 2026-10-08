import { queryOptions } from "@tanstack/react-query";
import { apiClient, type Envelope } from "@/shared/api";
import type {
  AdminCampaignSlug,
  CampaignRecipientCountResponse,
  RunCampaignResponse,
  SendTestCampaignResponse,
} from "./model/types";

/**
 * Клиент admin-кампаний уведомлений (#554 level-test, #704 email-only-login).
 *
 * Endpoint'ы живут под `/notifications/admin/campaigns/{slug}/*` (nginx проксирует
 * `/api/notifications/` → NotificationService). Trailing slash обязателен — иначе nginx
 * отдаёт 301 и ломает CORS preflight.
 */
export const adminCampaignsApi = {
  getRecipientCount: async (slug: AdminCampaignSlug, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<CampaignRecipientCountResponse>>(
      `/notifications/admin/campaigns/${slug}/recipient-count/`,
      { signal },
    );
    return res.data.result!;
  },

  sendTest: async (slug: AdminCampaignSlug) => {
    const res = await apiClient.post<Envelope<SendTestCampaignResponse>>(
      `/notifications/admin/campaigns/${slug}/test/`,
    );
    return res.data.result!;
  },

  run: async (slug: AdminCampaignSlug) => {
    const res = await apiClient.post<Envelope<RunCampaignResponse>>(
      `/notifications/admin/campaigns/${slug}/run/`,
    );
    return res.data.result!;
  },
};

export const adminCampaignsQueryOptions = {
  baseKey: "admin-campaigns",

  recipientCountKey: (slug: AdminCampaignSlug) =>
    ["admin-campaigns", slug, "recipient-count"] as const,

  recipientCount: (slug: AdminCampaignSlug) =>
    queryOptions({
      queryKey: adminCampaignsQueryOptions.recipientCountKey(slug),
      queryFn: ({ signal }) => adminCampaignsApi.getRecipientCount(slug, { signal }),
      // Audience size is an O(N) keyset scan over all users — don't refetch on every
      // page mount. The figure barely moves between admin visits.
      staleTime: 5 * 60 * 1000,
    }),
};
