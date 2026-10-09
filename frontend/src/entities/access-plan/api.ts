import { apiClient, type Envelope } from "@/shared/api";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  AccessStatusDto,
  AdminGrantRequest,
  CreateInviteRequest,
  CreatePlanRequest,
  InviteLinkDto,
  InvitePreviewDto,
  PlanDto,
  PlanGrantDto,
  PlanGrantsPageDto,
  PlanStatsDto,
  PublicPlanDto,
  SetPromotionRequest,
  UpdatePlanRequest,
  UpgradeQuoteDto,
  UserLookupResultDto,
} from "./types";

export const myGrantsQueryKey = ["access", "me", "grants"] as const;

export const accessPlanApi = {
  getPublicPlans: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<PublicPlanDto[]>>("/access/plans/public/", {
      signal,
    });
    return res.data;
  },

  getPublicPlanBySlug: async (slug: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<PublicPlanDto>>(`/access/plans/by-slug/${slug}`, {
      signal,
    });
    return res.data;
  },

  getInvitePreview: async (token: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<InvitePreviewDto>>(
      `/access/invites/${token}/preview`,
      { signal },
    );
    return res.data;
  },

  redeemInvite: async (token: string) => {
    const res = await apiClient.post<Envelope<PlanGrantDto>>(
      `/access/invites/${token}/redeem`,
      null,
    );
    return res.data;
  },

  getMyGrants: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<PlanGrantDto[]>>("/access/me/grants/", { signal });
    return res.data;
  },

  cancelAutoRenewal: async (grantId: string) => {
    const res = await apiClient.post<Envelope<PlanGrantDto>>(
      `/access/me/grants/${grantId}/cancel-renewal/`,
      null,
    );
    return res.data;
  },

  resumeAutoRenewal: async (grantId: string) => {
    const res = await apiClient.post<Envelope<PlanGrantDto>>(
      `/access/me/grants/${grantId}/resume-renewal/`,
      null,
    );
    return res.data;
  },

  getAccessStatus: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<AccessStatusDto>>("/access/me/access-status/", {
      signal,
    });
    return res.data;
  },

  // --- Author-side plan management (Phase D) ---

  getMyPlans: async ({ signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<PlanDto[]>>("/access/plans/", { signal });
    return res.data;
  },

  createPlan: async (request: CreatePlanRequest) => {
    const res = await apiClient.post<Envelope<string>>("/access/plans/", request);
    return res.data;
  },

  updatePlan: async (planId: string, request: UpdatePlanRequest) => {
    const res = await apiClient.patch<Envelope<unknown>>(`/access/plans/${planId}`, request);
    return res.data;
  },

  reorderPlans: async (orders: Array<{ planId: string; displayOrder: number }>) => {
    const res = await apiClient.post<Envelope<number>>("/access/plans/reorder/", { orders });
    return res.data;
  },

  publishPlan: async (planId: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/plans/${planId}/publish`, null);
    return res.data;
  },

  unpublishPlan: async (planId: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/plans/${planId}/unpublish`, null);
    return res.data;
  },

  archivePlan: async (planId: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/plans/${planId}/archive`, null);
    return res.data;
  },

  unarchivePlan: async (planId: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/plans/${planId}/unarchive`, null);
    return res.data;
  },

  // Полное (hard) удаление плана. Бэкенд отклонит (409) план с оплатами/активными доступами.
  deletePlan: async (planId: string) => {
    const res = await apiClient.delete<Envelope<unknown>>(`/access/plans/${planId}`);
    return res.data;
  },

  // Акции (промо-скидка на план)
  setPromotion: async (planId: string, request: SetPromotionRequest) => {
    const res = await apiClient.put<Envelope<string>>(
      `/access/plans/${planId}/promotion/`,
      request,
    );
    return res.data;
  },

  clearPromotion: async (planId: string) => {
    const res = await apiClient.delete<Envelope<string>>(`/access/plans/${planId}/promotion/`);
    return res.data;
  },

  // Invites
  getPlanInvites: async (planId: string, { signal }: { signal?: AbortSignal } = {}) => {
    const res = await apiClient.get<Envelope<InviteLinkDto[]>>(`/access/plans/${planId}/invites/`, {
      signal,
    });
    return res.data;
  },

  createInvite: async (planId: string, request: CreateInviteRequest) => {
    const res = await apiClient.post<Envelope<InviteLinkDto>>(
      `/access/plans/${planId}/invites/`,
      request,
    );
    return res.data;
  },

  revokeInvite: async (inviteId: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/invites/${inviteId}/revoke`, null);
    return res.data;
  },

  deleteInvite: async (inviteId: string) => {
    const res = await apiClient.delete<Envelope<string>>(`/access/invites/${inviteId}/`);
    return res.data;
  },

  // Grants
  getPlanGrants: async (
    planId: string,
    {
      cursor,
      limit,
      search,
      signal,
    }: { cursor?: string | null; limit?: number; search?: string; signal?: AbortSignal } = {},
  ) => {
    const params: Record<string, string | number> = {};
    if (cursor) params.cursor = cursor;
    if (limit) params.limit = limit;
    if (search) params.search = search;
    const res = await apiClient.get<Envelope<PlanGrantsPageDto>>(
      `/access/plans/${planId}/grants/`,
      { params, signal },
    );
    return res.data;
  },

  revokeGrant: async (grantId: string, reason?: string) => {
    const res = await apiClient.post<Envelope<unknown>>(`/access/grants/${grantId}/revoke`, {
      reason,
    });
    return res.data;
  },

  adminGrant: async (request: AdminGrantRequest) => {
    const res = await apiClient.post<Envelope<PlanGrantDto>>("/access/grants/admin/", request);
    return res.data;
  },

  // Per-plan stats dashboard (#289)
  getPlanStats: async (
    planId: string,
    { periodDays, signal }: { periodDays?: number; signal?: AbortSignal } = {},
  ) => {
    const params: Record<string, number> = {};
    if (periodDays !== undefined) params.periodDays = periodDays;
    const res = await apiClient.get<Envelope<PlanStatsDto>>(`/access/plans/${planId}/stats/`, {
      params,
      signal,
    });
    return res.data;
  },

  // User lookup для admin/author при ручной выдаче grant'а.
  lookupUsers: async (query: string, limit: number, { signal }: { signal?: AbortSignal } = {}) => {
    const params = new URLSearchParams({ q: query, limit: limit.toString() });
    const res = await apiClient.get<Envelope<UserLookupResultDto[]>>(
      `/access/users/lookup/?${params.toString()}`,
      { signal },
    );
    return res.data;
  },
};

export const publicPlansQueryOptions = () =>
  queryOptions({
    queryKey: ["access", "plans", "public"],
    queryFn: ({ signal }) => accessPlanApi.getPublicPlans({ signal }),
    select: (data) => data.result ?? [],
  });

export const publicPlanBySlugQueryOptions = (slug: string) =>
  queryOptions({
    queryKey: ["access", "plans", "public", slug],
    queryFn: ({ signal }) => accessPlanApi.getPublicPlanBySlug(slug, { signal }),
    select: (data) => data.result!,
  });

export const invitePreviewQueryOptions = (token: string) =>
  queryOptions({
    queryKey: ["access", "invites", token, "preview"],
    queryFn: ({ signal }) => accessPlanApi.getInvitePreview(token, { signal }),
    select: (data) => data.result!,
    staleTime: 30_000,
    retry: false,
  });

export const myGrantsQueryOptions = () =>
  queryOptions({
    queryKey: myGrantsQueryKey,
    queryFn: ({ signal }) => accessPlanApi.getMyGrants({ signal }),
    select: (data) => data.result ?? [],
  });

export const myPlansQueryOptions = () =>
  queryOptions({
    queryKey: ["access", "plans", "mine"],
    queryFn: ({ signal }) => accessPlanApi.getMyPlans({ signal }),
    select: (data) => data.result ?? [],
  });

/**
 * Состояние доступа текущего юзера для модалки «срок истёк» (#687). Поллится
 * глобальным `AccessExpiredOverlay`, сидит идлом пока нет недавно истёкшего доступа.
 */
export const accessStatusQueryOptions = () =>
  queryOptions({
    queryKey: ["access", "me", "status"],
    queryFn: ({ signal }) => accessPlanApi.getAccessStatus({ signal }),
    select: (data) => data.result ?? null,
    staleTime: 60_000,
  });

export const planInvitesQueryOptions = (planId: string) =>
  queryOptions({
    queryKey: ["access", "plans", planId, "invites"],
    queryFn: ({ signal }) => accessPlanApi.getPlanInvites(planId, { signal }),
    select: (data) => data.result ?? [],
  });

/**
 * Cursor-paginated grants list для author-side таба «Кому выдан доступ».
 * Использует `useInfiniteQuery` для скролла + поиска. Поиск дебаунсится в UI;
 * search-mode не пагинируется (backend возвращает один срез по результатам user-lookup).
 */
export const planGrantsInfiniteQueryOptions = (planId: string, search?: string) =>
  infiniteQueryOptions({
    queryKey: ["access", "plans", planId, "grants", { search: search ?? "" }] as const,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) =>
      accessPlanApi.getPlanGrants(planId, {
        cursor: pageParam ?? undefined,
        search,
        signal,
      }),
    getNextPageParam: (last) => last.result?.nextCursor ?? null,
    select: (data) => ({
      pages: data.pages,
      items: data.pages.flatMap((p) => p.result?.items ?? []),
      nextCursor: data.pages.at(-1)?.result?.nextCursor ?? null,
    }),
  });

export const planStatsQueryOptions = (planId: string, periodDays: number) =>
  queryOptions({
    queryKey: ["access", "plans", planId, "stats", { periodDays }] as const,
    queryFn: ({ signal }) => accessPlanApi.getPlanStats(planId, { periodDays, signal }),
    select: (data) => data.result!,
    staleTime: 30_000,
  });

export const userLookupQueryOptions = (query: string, limit = 10) =>
  queryOptions({
    queryKey: ["access", "users", "lookup", query, limit],
    queryFn: ({ signal }) => accessPlanApi.lookupUsers(query, limit, { signal }),
    select: (data) => data.result ?? [],
    enabled: query.trim().length >= 2,
    staleTime: 30_000,
    retry: false,
  });

export const upgradeQuoteQueryOptions = (planId: string | null | undefined) =>
  queryOptions({
    queryKey: ["access", "plans", planId, "upgrade-quote"] as const,
    queryFn: async ({ signal }) => {
      const res = await apiClient.get<Envelope<UpgradeQuoteDto>>(
        `/access/plans/${planId}/upgrade-quote/`,
        { signal },
      );
      return res.data;
    },
    select: (data) => data.result,
    enabled: Boolean(planId),
    staleTime: 30_000,
    retry: false,
  });
