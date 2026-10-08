import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduUserList = defineTool({
  name: 'edu_user_list',
  description:
    'List platform users (AuthService admin). Cursor-paginated by `created_at DESC, id DESC` — pass the returned ' +
    '`nextCursor` to fetch the next page (the `page` param is deprecated offset-mode, kept for back-compat). ' +
    'Filters: `search` (matches email / username / display_name, ILIKE), `role` (exact role name, e.g. `platform-author`), ' +
    '`status` (active | locked | unconfirmed), `createdAfter`/`createdBefore`/`lastLoginAfter`/`lastLoginBefore` (ISO-8601). ' +
    'Returns `{ items: [{ id, userName, displayName, email, emailConfirmed, roles, isLockedOut, createdAt, lastLoginAt, avatarId }], nextCursor, totalCount }`. ' +
    'Requires `users.view`.',
  inputSchema: z
    .object({
      search: z.string().max(100).optional(),
      role: z.string().optional(),
      status: z.enum(['active', 'locked', 'unconfirmed']).optional(),
      cursor: z.string().optional(),
      pageSize: z.number().int().min(1).max(100).optional(),
      page: z.number().int().min(1).optional(),
      createdAfter: z.string().optional(),
      createdBefore: z.string().optional(),
      lastLoginAfter: z.string().optional(),
      lastLoginBefore: z.string().optional(),
    })
    .strict(),
  handler: async (
    {
      search,
      role,
      status,
      cursor,
      pageSize,
      page,
      createdAfter,
      createdBefore,
      lastLoginAfter,
      lastLoginBefore,
    },
    { client },
  ) => {
    const { data } = await client.get<unknown>('/api/users/', {
      query: {
        search,
        role,
        status,
        cursor,
        pageSize,
        page,
        createdAfter,
        createdBefore,
        lastLoginAfter,
        lastLoginBefore,
      },
    });
    return data;
  },
});

export const eduUserDetail = defineTool({
  name: 'edu_user_detail',
  description:
    'Fetch a single user profile (AuthService admin): id, userName, displayName, email, emailConfirmed, roles, ' +
    'isLockedOut, lockoutEnd, createdAt, updatedAt, lastLoginAt, bio, role-profiles, avatarId. Requires `users.view`.',
  inputSchema: z.object({ userId: z.string().uuid() }).strict(),
  handler: async ({ userId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/users/${userId}`);
    return data;
  },
});

export const eduUserStats = defineTool({
  name: 'edu_user_stats',
  description:
    'Aggregate user KPIs (AuthService admin dashboard): total, newToday/Week/Month, activeWeek (by last_login_at), ' +
    'confirmed, locked, byRole map, and a dense daily-registration timeseries for the requested window. ' +
    'Optional `from`/`to` (YYYY-MM-DD) bound the timeseries window (default last 30 days, max 366). Requires `users.view`.',
  inputSchema: z
    .object({
      from: z.string().optional(),
      to: z.string().optional(),
    })
    .strict(),
  handler: async ({ from, to }, { client }) => {
    const { data } = await client.get<unknown>('/api/users/admin/stats', {
      query: { from, to },
    });
    return data;
  },
});

export const eduUserSetRoles = defineTool({
  name: 'edu_user_set_roles',
  description:
    'Replace the FULL set of roles on a user (AuthService admin). The `roles` array is authoritative — roles not ' +
    'listed are removed, roles present are added. Valid role names: `platform-participant`, `platform-author`, ' +
    '`platform-moderator`, `platform-admin`, `platform-service`. Revokes all the user\'s active tokens afterwards. ' +
    'You cannot remove `platform-admin` from yourself. Requires `users.manage`. Destructive — requires confirm = userId.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      roles: z.array(z.string()).min(1),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ userId, roles, confirm }, { client }) => {
    requireConfirm('edu_user_set_roles', userId, confirm);
    const { data } = await client.put<string>(`/api/users/${userId}/roles`, { roles });
    return { userId, result: data };
  },
});

export const eduUserLockout = defineTool({
  name: 'edu_user_lockout',
  description:
    'Lock or unlock a user account (AuthService admin). `isLocked=true` blocks sign-in (and revokes the user\'s active ' +
    'tokens); pass optional `lockoutEnd` (ISO-8601, must be in the future) for a temporary lock — omit it for a ' +
    'permanent lock. `isLocked=false` unlocks. You cannot lock yourself. Requires `users.manage`. ' +
    'Destructive — requires confirm = userId.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      isLocked: z.boolean(),
      lockoutEnd: z.string().optional(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ userId, isLocked, lockoutEnd, confirm }, { client }) => {
    requireConfirm('edu_user_lockout', userId, confirm);
    const { data } = await client.post<string>(`/api/users/${userId}/lockout`, {
      isLocked,
      lockoutEnd,
    });
    return { userId, result: data };
  },
});
