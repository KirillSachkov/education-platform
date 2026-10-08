import { apiClient, type Envelope, nextPageParamFromPagination } from "@/shared/api";
import { infiniteQueryOptions, queryOptions } from "@tanstack/react-query";
import type {
  AdminAuditLogPage,
  AdminAuditLogQuery,
  AdminBulkActionResponse,
  AdminBulkLockoutRequest,
  AdminBulkRolesRequest,
  AdminCreateUserRequest,
  AdminSetLockoutRequest,
  AdminSetPasswordRequest,
  AdminSetRolesRequest,
  AdminStats,
  AdminStatsRange,
  AdminUpdateUserRequest,
  AdminUserDetail,
  GetUsersParams,
  GetUsersResponse,
} from "./types";

export const usersAdminApi = {
  getUsers: async (params: GetUsersParams, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<GetUsersResponse>>("/users/", {
      params,
      signal,
    });
    return res.data;
  },

  getUserDetail: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminUserDetail>>(`/users/${userId}`, { signal });
    return res.data;
  },

  getAdminStats: async (range: AdminStatsRange | undefined, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminStats>>("/users/admin/stats", {
      params: range,
      signal,
    });
    return res.data;
  },

  getAdminAuditLog: async (query: AdminAuditLogQuery, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminAuditLogPage>>("/users/admin/audit-log", {
      params: query,
      signal,
    });
    return res.data;
  },

  createUser: async (request: AdminCreateUserRequest) => {
    const res = await apiClient.post<Envelope<string>>("/users/", request);
    return res.data;
  },

  updateUser: async (userId: string, request: AdminUpdateUserRequest) => {
    const res = await apiClient.patch<Envelope<string>>(`/users/${userId}`, request);
    return res.data;
  },

  setPassword: async (userId: string, request: AdminSetPasswordRequest) => {
    const res = await apiClient.post<Envelope<string>>(`/users/${userId}/password`, request);
    return res.data;
  },

  setRoles: async (userId: string, request: AdminSetRolesRequest) => {
    const res = await apiClient.put<Envelope<string>>(`/users/${userId}/roles`, request);
    return res.data;
  },

  setLockout: async (userId: string, request: AdminSetLockoutRequest) => {
    const res = await apiClient.post<Envelope<string>>(`/users/${userId}/lockout`, request);
    return res.data;
  },

  deleteUser: async (userId: string) => {
    const res = await apiClient.delete<Envelope<string>>(`/users/${userId}`);
    return res.data;
  },

  bulkLockout: async (request: AdminBulkLockoutRequest) => {
    const res = await apiClient.post<Envelope<AdminBulkActionResponse>>(
      "/users/admin/bulk/lockout",
      request,
    );
    return res.data;
  },

  bulkRoles: async (request: AdminBulkRolesRequest) => {
    const res = await apiClient.post<Envelope<AdminBulkActionResponse>>(
      "/users/admin/bulk/roles",
      request,
    );
    return res.data;
  },

  buildExportCsvUrl: (params: GetUsersParams): string => {
    const query = new URLSearchParams();
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== "") {
        query.set(key, String(value));
      }
    });
    const qs = query.toString();
    return `/users/admin/export.csv${qs ? `?${qs}` : ""}`;
  },

  revokeSessions: async () => {
    await apiClient.post("/users/me/sessions/revoke-all");
  },

  unlinkGitHub: async () => {
    await apiClient.post("/users/me/github/unlink");
  },

  getTelegramLinkToken: async () => {
    const res = await apiClient.get<
      Envelope<{ linkToken: string; botUsername: string; deepLinkUrl: string }>
    >("/users/me/telegram/link-token/");
    return res.data.result!;
  },

  unlinkTelegram: async () => {
    await apiClient.post("/users/me/telegram/unlink/");
  },

  syncIntegrations: async () => {
    const res = await apiClient.post<
      Envelope<{
        matchedGithubOrgs: string[];
        githubSyncTriggered: boolean;
        telegramLinked: boolean;
      }>
    >("/users/me/integrations/sync/");
    return res.data.result!;
  },

  resyncTelegramInvites: async () => {
    const res = await apiClient.post<Envelope<{ invitesSent: number; telegramLinked: boolean }>>(
      "/telegram/me/resync-invites/",
    );
    return res.data.result!;
  },
};

export const usersQueryOptions = {
  baseKey: "admin-users",

  getUsersKey: (params: GetUsersParams) => [usersQueryOptions.baseKey, "list", params] as const,

  getUsersOptions: (params: GetUsersParams) =>
    queryOptions({
      queryKey: usersQueryOptions.getUsersKey(params),
      queryFn: ({ signal }) => usersAdminApi.getUsers(params, signal),
      select: (data) => data.result,
    }),

  getUserDetailKey: (userId: string) => [usersQueryOptions.baseKey, "detail", userId] as const,

  getUserDetailOptions: (userId: string) =>
    queryOptions({
      queryKey: usersQueryOptions.getUserDetailKey(userId),
      queryFn: ({ signal }) => usersAdminApi.getUserDetail(userId, signal),
      select: (data) => data.result,
    }),

  getAdminStatsKey: (range?: AdminStatsRange) =>
    [usersQueryOptions.baseKey, "stats", range ?? null] as const,

  getAdminStatsOptions: (range?: AdminStatsRange) =>
    queryOptions({
      queryKey: usersQueryOptions.getAdminStatsKey(range),
      queryFn: ({ signal }) => usersAdminApi.getAdminStats(range, signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getAdminAuditLogKey: (query: AdminAuditLogQuery) =>
    [usersQueryOptions.baseKey, "audit-log", query] as const,

  getAdminAuditLogOptions: (query: AdminAuditLogQuery) =>
    queryOptions({
      queryKey: usersQueryOptions.getAdminAuditLogKey(query),
      queryFn: ({ signal }) => usersAdminApi.getAdminAuditLog(query, signal),
      select: (data) => data.result,
    }),

  searchUsersKey: (search: string) => [usersQueryOptions.baseKey, "search", search] as const,

  searchUsersOptions: (search: string) =>
    queryOptions({
      queryKey: usersQueryOptions.searchUsersKey(search),
      queryFn: ({ signal }) => usersAdminApi.getUsers({ search, pageSize: 20 }, signal),
      select: (data) => data.result?.items ?? [],
      enabled: search.length >= 1,
    }),

  searchUsersInfiniteKey: (search: string, role?: string, pageSize?: number) =>
    [usersQueryOptions.baseKey, "search-infinite", { search, role, pageSize }] as const,

  searchUsersInfiniteOptions: (search: string, opts?: { role?: string; pageSize?: number }) => {
    const pageSize = opts?.pageSize ?? 20;
    const role = opts?.role;
    return infiniteQueryOptions({
      queryKey: usersQueryOptions.searchUsersInfiniteKey(search, role, pageSize),
      queryFn: ({ pageParam, signal }) =>
        usersAdminApi.getUsers(
          { search: search || undefined, role, page: pageParam, pageSize },
          signal,
        ),
      initialPageParam: 1,
      getNextPageParam: (lastPage) => nextPageParamFromPagination(lastPage),
      select: (data) => ({
        pages: data.pages,
        pageParams: data.pageParams,
        items: data.pages.flatMap((p) => p.result?.items ?? []),
        totalCount: data.pages[0]?.result?.totalCount ?? 0,
      }),
    });
  },
};
