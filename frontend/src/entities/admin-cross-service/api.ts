import { apiClient, type Envelope } from "@/shared/api";
import { queryOptions } from "@tanstack/react-query";

// ----- ProgressService admin reads -----

export type AdminUserEnrollment = {
  courseId: string;
  authorId: string | null;
  enrolledAt: string;
  source: string;
};

export type AdminUserSubmission = {
  id: string;
  issueId: string;
  courseId: string;
  reviewStatus: string;
  submittedAt: string;
  reviewedAt: string | null;
  attemptNumber: number;
  latestAiVerdict: string | null;
  aiIterationsCount: number;
  aiReviewStatus: string | null;
  readyForHumanReview: boolean;
};

export type AdminProgressStats = {
  totalEnrollments: number;
  activeEnrollments: number;
  newEnrollmentsThisWeek: number;
  activeUsersThisWeek: number;
  totalSubmissions: number;
  submissionsThisWeek: number;
  topCourses: { courseId: string; enrollmentCount: number }[];
  rangeFrom: string;
  rangeTo: string;
  newEnrollmentsInRange: number;
  activeUsersInRange: number;
  submissionsInRange: number;
};

export type AdminLevelTestOverview = {
  totalAttempts: number;
  userAttempts: number;
  uniqueUsers: number;
  anonymousAttempts: number;
  attemptsLast7Days: number;
  averagePercent: number;
  levelDistribution: { level: string; count: number }[];
  sectionAverages: { key: string; title: string; averagePercent: number; attempts: number }[];
  attemptsByDay: { day: string; count: number }[];
};

export type AdminLevelTestAttemptRow = {
  attemptId: string;
  userId: string | null;
  username: string | null;
  displayName: string | null;
  overallPercent: number;
  level: string;
  answeredCount: number;
  totalQuestions: number;
  aiGradingStatus: string;
  isClaimed: boolean;
  createdAt: string;
};

export type AdminLevelTestAttempts = {
  items: AdminLevelTestAttemptRow[];
  totalCount: number;
  offset: number;
  limit: number;
};

/**
 * Query params shared by `/users/admin/stats`, `/access/admin/stats`,
 * `/progress/admin/stats`. ISO `YYYY-MM-DD`. Backend defaults to a trailing
 * 30-day window when omitted.
 */
export type AdminStatsRangeParams = {
  from?: string;
  to?: string;
};

// ----- AccessService admin reads -----

export type AdminUserGrant = {
  id: string;
  /** Pinned-имя из контракта #414 (== id; обе колонки алиасят `plan_grants` PK). */
  grantId: string;
  planId: string;
  planDisplayName: string | null;
  planTier: string | null;
  planAuthorId: string;
  source: string;
  status: string;
  createdAt: string;
  /** Pinned-имя из контракта #414 (== createdAt; обе алиасят `granted_at`). */
  grantedAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  revokedReason: string | null;
};

export type AdminUserOrder = {
  id: string;
  planId: string;
  planDisplayName: string | null;
  amountCents: number;
  currency: string;
  status: string;
  provider: string;
  externalProviderRef: string | null;
  createdAt: string;
  paidAt: string | null;
  failureReason: string | null;
};

export type AdminAccessStats = {
  totalRevenueCents: number;
  paidOrdersCount: number;
  failedOrdersCount: number;
  activeGrantsCount: number;
  expiredGrantsCount: number;
  revokedGrantsCount: number;
  topPlans: { planId: string; displayName: string | null; activeGrantsCount: number }[];
  rangeFrom: string;
  rangeTo: string;
  revenueInRangeCents: number;
  paidOrdersInRangeCount: number;
  failedOrdersInRangeCount: number;
};

// ----- Active grants batch (admin user list) -----

export type AdminActiveGrantSummary = {
  id: string;
  userId: string;
  planId: string;
  planDisplayName: string | null;
  planTier: string | null;
  planAuthorId: string;
  source: string;
  expiresAt: string | null;
};

export type AdminActiveGrantsByUsers = {
  grants: Record<string, AdminActiveGrantSummary[]>;
};

// ----- Post-purchase status (AccessService, #444) -----

/**
 * Telegram membership status of a grantee inside a plan's bound chat(s).
 * `n/a` — план не привязан к Telegram-чату вовсе; `unknown` — TBS недоступен.
 */
export type AdminTelegramMembershipStatus = "member" | "not_member" | "unknown" | "n/a";

export type AdminPostPurchaseTelegram = {
  chatBound: boolean;
  isMember: boolean;
  status: AdminTelegramMembershipStatus;
};

export type AdminPostPurchaseOnboarding = {
  flowEnabled: boolean;
  started: boolean;
  completed: boolean;
  totalSteps: number;
  completedSteps: number;
  skippedSteps: number;
  currentStepType: string | null;
  telegramStepPending: boolean;
  githubStepPending: boolean;
};

export type AdminPostPurchaseOrder = {
  id: string;
  planId: string;
  planDisplayName: string | null;
  amountCents: number;
  status: string;
  createdAt: string;
  paidAt: string | null;
};

export type AdminPostPurchaseGrant = {
  grantId: string;
  planId: string;
  planDisplayName: string | null;
  planTier: string | null;
  source: string;
  grantedAt: string;
  onboarding: AdminPostPurchaseOnboarding | null;
  telegram: AdminPostPurchaseTelegram;
};

export type AdminPostPurchaseStatus = {
  userId: string;
  hasPaidOrder: boolean;
  orders: AdminPostPurchaseOrder[];
  activeGrants: AdminPostPurchaseGrant[];
  diagnostics: string[];
};

// ----- Telegram admin reads (#444) -----

export type AdminTelegramLink = {
  linked: boolean;
  telegramUserId: number | null;
  telegramUsername: string | null;
  linkedAt: string | null;
};

export type AdminPlanChat = {
  telegramChatId: number;
  chatTitle: string | null;
  chatType: string;
  inviteLink: string | null;
  enrollmentGrantsMembership: boolean;
};

export type AdminPlanChats = {
  chats: AdminPlanChat[];
};

// ----- Support-action results (#444) -----

export type AdminRecheckTelegramResult = {
  completed: boolean;
  status: string;
};

/**
 * outcome ∈ Sent | AlreadySent | NoWelcomeConfigured | SendFailed | NotLinked | NoChatBound.
 */
export type AdminResendWelcomeResult = {
  outcome: string;
};

export type AdminResyncInvitesResult = {
  invitesSent: number;
  telegramLinked: boolean;
};

// ----- GitHub / App status (#444) -----

export type AdminUserGithubOrg = {
  slug: string;
  syncedAt: string;
};

/** GitHub-привязка юзера (AuthService): linked + login id/username + cached orgs. */
export type AdminUserGithubStatus = {
  linked: boolean;
  githubUserId: string | null;
  githubUsername: string | null;
  orgs: AdminUserGithubOrg[];
};

export type AdminVcsInstallationStatus = "ACTIVE" | "SUSPENDED" | "UNINSTALLED";

export type AdminVcsInstallationOwnerType = "USER" | "ORG";

/**
 * GitHub App (AI-review) installation юзера — снимок из AssignmentReviewService.
 * Совпадает с контрактом `VcsInstallationDto` (entities/vcs-installation), но
 * объявлен локально, чтобы не тянуть cross-entity import (FSD-границы).
 */
export type AdminVcsInstallation = {
  id: string;
  provider: string;
  ownerLogin: string;
  ownerType: AdminVcsInstallationOwnerType;
  status: AdminVcsInstallationStatus;
  allRepos: boolean;
  repos: string[];
  installedAt: string;
  removedAt: string | null;
};

// ----- CommentService admin reads -----

export type AdminUserComment = {
  id: string;
  entityType: string;
  entityId: string;
  parentId: string | null;
  bodyPreview: string;
  createdAt: string;
  updatedAt: string | null;
  deletedAt: string | null;
};

// ----- API client -----

export const adminCrossServiceApi = {
  getEnrollments: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ items: AdminUserEnrollment[] }>>(
      `/progress/admin/users/${userId}/enrollments`,
      { signal },
    );
    return res.data;
  },

  getSubmissions: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ items: AdminUserSubmission[] }>>(
      `/progress/admin/users/${userId}/submissions`,
      { signal },
    );
    return res.data;
  },

  getProgressStats: async (range: AdminStatsRangeParams | undefined, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminProgressStats>>("/progress/admin/stats", {
      params: range,
      signal,
    });
    return res.data;
  },

  getLevelTestOverview: async (signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminLevelTestOverview>>(
      "/progress/level-test/admin/overview/",
      { signal },
    );
    return res.data;
  },

  getLevelTestAttempts: async (offset: number, limit: number, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminLevelTestAttempts>>(
      "/progress/level-test/admin/attempts/",
      { params: { offset, limit }, signal },
    );
    return res.data;
  },

  getGrants: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ items: AdminUserGrant[] }>>(
      `/access/admin/users/${userId}/grants`,
      { signal },
    );
    return res.data;
  },

  getOrders: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ items: AdminUserOrder[] }>>(
      `/access/admin/users/${userId}/orders`,
      { signal },
    );
    return res.data;
  },

  /**
   * Ручной отзыв plan-grant'а у юзера администратором (#414). `reason` обязателен —
   * попадает в audit-trail. Бэкенд снимает access-tag'и из Redis и публикует
   * `plan_grant.revoked`. Контракт: `POST /access/admin/grants/{grantId}/revoke`
   * (no-slash — совпадает с backend-роутом и sibling `/access/grants/{id}/revoke`;
   * у этого семейства роутов trailing-slash нет, ASP.NET его не толерирует → 404).
   */
  revokeGrant: async (grantId: string, reason: string) => {
    const res = await apiClient.post<Envelope<unknown>>(
      `/access/admin/grants/${grantId}/revoke`,
      { reason },
    );
    return res.data;
  },

  /**
   * Legacy admin override для «зачёта месячного доступа» (#580/#604). Paid trial credit
   * больше не сгорает; endpoint оставлен для совместимости операций/админки.
   * `until` опционален (ISO) — пусто => бэкенд ставит дефолт +30д. Trailing slash —
   * совпадает с backend-роутом `POST /access/admin/users/{userId}/trial-credit-override/`.
   */
  trialCreditOverride: async (userId: string, planId: string, until?: string | null) => {
    const res = await apiClient.post<Envelope<unknown>>(
      `/access/admin/users/${userId}/trial-credit-override/`,
      { planId, until: until ?? null },
    );
    return res.data;
  },

  getAccessStats: async (range: AdminStatsRangeParams | undefined, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminAccessStats>>("/access/admin/stats", {
      params: range,
      signal,
    });
    return res.data;
  },

  getActiveGrantsByUsers: async (userIds: string[], signal?: AbortSignal) => {
    const res = await apiClient.post<Envelope<AdminActiveGrantsByUsers>>(
      "/access/admin/grants/by-users/",
      { userIds },
      { signal },
    );
    return res.data;
  },

  getRecentComments: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ items: AdminUserComment[] }>>(
      `/comments/admin/users/${userId}/recent`,
      { signal },
    );
    return res.data;
  },

  /**
   * Сводка post-purchase по юзеру (#444): оплаченные заказы, активные гранты с
   * onboarding-прогрессом и Telegram-членством, diagnostics. NO trailing slash —
   * совпадает с backend-роутом существующего эндпоинта.
   */
  getPostPurchaseStatus: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminPostPurchaseStatus>>(
      `/access/admin/users/${userId}/post-purchase-status`,
      { signal },
    );
    return res.data;
  },

  /** Telegram-привязка юзера (#444). Trailing slash — совпадает с TBS-роутом. */
  getUserTelegramLink: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminTelegramLink>>(
      `/telegram/admin/users/${userId}/link/`,
      { signal },
    );
    return res.data;
  },

  /** Telegram-чаты, привязанные к плану (#444). Trailing slash — совпадает с TBS-роутом. */
  getPlanTelegramChats: async (planId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminPlanChats>>(
      `/telegram/admin/plans/${planId}/chats/`,
      { signal },
    );
    return res.data;
  },

  // ----- Safe support actions (#444) -----

  /**
   * Перепроверить Telegram-членство юзера в чате плана и докрутить onboarding
   * (AccessService). Soft-degrade: если TBS недоступен — no-op, completed=false.
   */
  recheckTelegramMembership: async (userId: string, planId: string) => {
    const res = await apiClient.post<Envelope<AdminRecheckTelegramResult>>(
      `/access/admin/users/${userId}/plans/${planId}/telegram/recheck/`,
    );
    return res.data;
  },

  /** Повторно отправить приветствие плана юзеру в Telegram DM (TBS, force). */
  resendPlanWelcome: async (userId: string, planId: string) => {
    const res = await apiClient.post<Envelope<AdminResendWelcomeResult>>(
      `/telegram/admin/users/${userId}/plans/${planId}/welcome/resend/`,
    );
    return res.data;
  },

  /** Переотправить Telegram invite-ссылки юзеру по всем его активным грантам (TBS). */
  resyncTelegramInvites: async (userId: string) => {
    const res = await apiClient.post<Envelope<AdminResyncInvitesResult>>(
      `/telegram/admin/users/${userId}/resync-invites/`,
    );
    return res.data;
  },

  // ----- GitHub / App status reads (#444) -----

  /** GitHub-привязка юзера (AuthService). No trailing slash — совпадает с backend-роутом. */
  getUserGithubStatus: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<AdminUserGithubStatus>>(
      `/users/${userId}/github-status`,
      { signal },
    );
    return res.data;
  },

  /** GitHub App (AI-review) installation'ы юзера (AssignmentReviewService). Trailing slash. */
  getUserVcsInstallations: async (userId: string, signal?: AbortSignal) => {
    const res = await apiClient.get<Envelope<{ installations: AdminVcsInstallation[] }>>(
      `/assignment-review/admin/users/${userId}/installations/`,
      { signal },
    );
    return res.data;
  },
};

export const adminCrossServiceQueryOptions = {
  baseKey: "admin-cross-service",

  getEnrollmentsOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "enrollments", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getEnrollments(userId, signal),
      select: (data) => data.result?.items ?? [],
    }),

  getSubmissionsOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "submissions", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getSubmissions(userId, signal),
      select: (data) => data.result?.items ?? [],
    }),

  getProgressStatsOptions: (range?: AdminStatsRangeParams) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "progress-stats", range ?? null] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getProgressStats(range, signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getLevelTestOverviewOptions: () =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "level-test-overview"] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getLevelTestOverview(signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getLevelTestAttemptsOptions: (offset: number, limit: number) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "level-test-attempts", offset, limit] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getLevelTestAttempts(offset, limit, signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getGrantsOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "grants", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getGrants(userId, signal),
      select: (data) => data.result?.items ?? [],
    }),

  getOrdersOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "orders", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getOrders(userId, signal),
      select: (data) => data.result?.items ?? [],
    }),

  getAccessStatsOptions: (range?: AdminStatsRangeParams) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "access-stats", range ?? null] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getAccessStats(range, signal),
      select: (data) => data.result,
      staleTime: 30_000,
    }),

  getRecentCommentsOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "comments", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getRecentComments(userId, signal),
      select: (data) => data.result?.items ?? [],
    }),

  getPostPurchaseStatusOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "post-purchase-status", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getPostPurchaseStatus(userId, signal),
      select: (data) => data.result,
    }),

  getUserTelegramLinkOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "telegram-link", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getUserTelegramLink(userId, signal),
      select: (data) => data.result,
    }),

  getPlanTelegramChatsOptions: (planId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "plan-telegram-chats", planId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getPlanTelegramChats(planId, signal),
      select: (data) => data.result?.chats ?? [],
    }),

  getUserGithubStatusOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "github-status", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getUserGithubStatus(userId, signal),
      select: (data) => data.result,
    }),

  getUserVcsInstallationsOptions: (userId: string) =>
    queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "vcs-installations", userId] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getUserVcsInstallations(userId, signal),
      select: (data) => data.result?.installations ?? [],
    }),

  getActiveGrantsByUsersOptions: (userIds: readonly string[]) => {
    const sortedIds = [...userIds].sort();
    return queryOptions({
      queryKey: [adminCrossServiceQueryOptions.baseKey, "active-grants-by-users", sortedIds] as const,
      queryFn: ({ signal }) => adminCrossServiceApi.getActiveGrantsByUsers(sortedIds, signal),
      enabled: sortedIds.length > 0,
      select: (data) => data.result?.grants ?? {},
      staleTime: 30_000,
    });
  },
};
