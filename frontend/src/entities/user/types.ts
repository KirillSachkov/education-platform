export type AdminUserSummary = {
  id: string;
  userName: string | null;
  displayName: string | null;
  email: string | null;
  emailConfirmed: boolean;
  roles: string[];
  isLockedOut: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  avatarId: string | null;
};

export type AdminUserDetail = {
  id: string;
  userName: string | null;
  displayName: string | null;
  email: string | null;
  emailConfirmed: boolean;
  roles: string[];
  isLockedOut: boolean;
  lockoutEnd: string | null;
  createdAt: string;
  updatedAt: string;
  lastLoginAt: string | null;
  bio: string | null;
  profiles: {
    student: unknown | null;
    author: unknown | null;
    reviewer: unknown | null;
  } | null;
  avatarId: string | null;
  /** Telegram @handle (provider_display_name), null если Telegram не привязан — #575. */
  telegramUsername: string | null;
};

export type DailyRegistration = {
  day: string;
  count: number;
};

/**
 * Common query params for admin stats endpoints (`/users/admin/stats`,
 * `/access/admin/stats`, `/progress/admin/stats`). Dates are ISO `YYYY-MM-DD`.
 * Backend defaults to a trailing 30-day window when both are absent.
 */
export type AdminStatsRange = {
  from?: string;
  to?: string;
};

export type AdminStats = {
  totalUsers: number;
  newToday: number;
  newThisWeek: number;
  newThisMonth: number;
  activeThisWeek: number;
  confirmedEmailCount: number;
  lockedCount: number;
  byRole: Record<string, number>;
  dailyRegistrations: DailyRegistration[];
  rangeFrom: string;
  rangeTo: string;
  newInRange: number;
};

export type AdminAuditLogEntry = {
  id: string;
  adminId: string;
  adminUsername: string | null;
  adminDisplayName: string | null;
  targetUserId: string | null;
  action: string;
  method: string;
  path: string;
  payloadJson: string | null;
  result: "success" | "failure" | string;
  errorMessage: string | null;
  createdAt: string;
  ipAddress: string | null;
  userAgent: string | null;
};

export type AdminAuditLogPage = {
  items: AdminAuditLogEntry[];
  nextCursor: string | null;
};

export type AdminAuditLogQuery = {
  targetUserId?: string;
  adminId?: string;
  action?: string;
  from?: string;
  to?: string;
  cursor?: string;
  pageSize?: number;
};

export type GetUsersResponse = {
  items: AdminUserSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
};

export type GetUsersParams = {
  page?: number;
  pageSize?: number;
  search?: string;
  role?: string;
  createdAfter?: string;
  createdBefore?: string;
  status?: "active" | "locked" | "unconfirmed";
  lastLoginAfter?: string;
  lastLoginBefore?: string;
};

export type AdminBulkLockoutRequest = {
  userIds: string[];
  isLocked: boolean;
  lockoutEnd?: string;
};

export type AdminBulkRolesRequest = {
  userIds: string[];
  add: string[];
  remove: string[];
};

export type AdminBulkActionFailure = {
  userId: string;
  reason: string;
};

export type AdminBulkActionResponse = {
  succeeded: string[];
  failed: AdminBulkActionFailure[];
};

export type AdminCreateUserRequest = {
  email: string;
  username: string;
  password: string;
  roles: string[];
};

export type AdminUpdateUserRequest = {
  username?: string;
  email?: string;
  emailConfirmed?: boolean;
};

export type AdminSetPasswordRequest = {
  newPassword: string;
};

export type AdminSetRolesRequest = {
  roles: string[];
};

export type AdminSetLockoutRequest = {
  isLocked: boolean;
  lockoutEnd?: string;
};
