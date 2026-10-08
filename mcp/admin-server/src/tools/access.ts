import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduPlanList = defineTool({
  name: 'edu_plan_list',
  description:
    'List the calling author\'s plans (AccessService) — every plan owned by the authenticated principal, any state ' +
    '(draft / public, active / archived). Returns full `PlanDto[]`: id, authorId, tier (LEARN_ALL | FULL_ALL | COURSE | ' +
    'SUBSCRIPTION), slug, displayName, price, discount/promotion fields, courseId, capabilities, isPublic, isActive, ' +
    'archivedAt, githubOrgSlug, etc. Use this to discover plan IDs before listing grants or issuing invites. ' +
    'Requires `plans.manage`.',
  inputSchema: z.object({}).strict(),
  handler: async (_input, { client }) => {
    const { data } = await client.get<unknown[]>('/api/access/plans/');
    return { plans: data, count: data.length };
  },
});

export const eduPlanDelete = defineTool({
  name: 'edu_plan_delete',
  description:
    'Permanently (hard) delete a plan by its ID (AccessService). Cascades away the plan and everything that ' +
    'references it — plan-courses, invite links + redemptions, non-paid orders, non-active grants, onboarding ' +
    'flow + pinned materials — and unbinds its Telegram chat-bindings. BLOCKED with 409 ' +
    '(`plan.delete.has_paid_orders` / `plan.delete.has_active_grants`) if the plan ever had a paid order or has ' +
    'any active grant — archive it instead (POST /access/plans/{id}/archive). Irreversible, no soft-delete. ' +
    'Use `edu_plan_list` to find the plan ID. Requires `plans.manage` + plan ownership (admin bypasses). ' +
    'Destructive — requires confirm = planId.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ planId, confirm }, { client }) => {
    requireConfirm('edu_plan_delete', planId, confirm);
    const { data } = await client.delete<string>(`/api/access/plans/${planId}`);
    return { planId: data };
  },
});

export const eduPlanSetTelegramWelcome = defineTool({
  name: 'edu_plan_set_telegram_welcome',
  description:
    'Set (or clear) the Telegram welcome message for a plan (AccessService). The bot posts this message IN the bound ' +
    'Telegram group when a new member is approved into it, pointing them to the platform. Markdown is supported; max ' +
    '4096 characters (one Telegram message). Pass an empty string to clear the welcome (bot then posts nothing on join). ' +
    'Use `edu_plan_list` to find the plan ID. Requires `plans.manage` + plan ownership (admin bypasses).',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      message: z.string().max(4096),
    })
    .strict(),
  handler: async ({ planId, message }, { client }) => {
    const { data } = await client.patch<unknown>(`/api/access/plans/${planId}`, {
      telegramWelcomeMessage: message,
    });
    return data;
  },
});

export const eduPlanGrantsList = defineTool({
  name: 'edu_plan_grants_list',
  description:
    'List grants issued on a plan (AccessService), keyset-paginated by `granted_at DESC`. Each item is enriched with ' +
    'the grantee\'s user info (email / name / avatar via AuthService — soft-degrades to nulls if AuthService is down). ' +
    'Optional `search` narrows to grantees matching a name/email/username; `limit` caps page size (default 20, max 100); ' +
    'pass the returned `nextCursor` for the next page. Requires `plans.manage` + plan ownership (admin bypasses). ' +
    'Returns `{ items: [PlanGrantDto], nextCursor }`.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      search: z.string().optional(),
      limit: z.number().int().min(1).max(100).optional(),
      cursor: z.string().optional(),
    })
    .strict(),
  handler: async ({ planId, search, limit, cursor }, { client }) => {
    const { data } = await client.get<unknown>(`/api/access/plans/${planId}/grants/`, {
      query: { search, limit, cursor },
    });
    return data;
  },
});

export const eduUserPostPurchaseStatus = defineTool({
  name: 'edu_user_post_purchase_status',
  description:
    'Consolidated post-purchase support snapshot for one user (AccessService, #444). Returns paid/failed orders, ' +
    'active plan grants, per-plan onboarding state (flow enabled, started/completed, pending Telegram/GitHub steps), ' +
    'live Telegram chat membership (member | not_member | unknown | n/a — verified via TelegramBotService, soft-degrades ' +
    'to unknown when it is down), and human-readable diagnostics (e.g. "paid order but no active grant", "Telegram ' +
    'membership not confirmed"). Read-only; replaces manually digging through prod DB + Bot API for the "bought a course ' +
    'but did not get into the Telegram group" case. Requires role admin or moderator. ' +
    'Returns { userId, hasPaidOrder, orders[], activeGrants[], diagnostics[] }.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ userId }, { client }) => {
    const { data } = await client.get<unknown>(
      `/api/access/admin/users/${userId}/post-purchase-status`,
    );
    return data;
  },
});

export const eduUserRecheckTelegramMembership = defineTool({
  name: 'edu_user_recheck_telegram_membership',
  description:
    'Force a live re-check of one user\'s Telegram group membership for a plan and advance their onboarding ' +
    '(AccessService, support action, #444) — admin variant of the user-facing "recheck my Telegram membership". ' +
    'Asks TelegramBotService whether the user is currently a member of the plan\'s bound chat; if they are AND the ' +
    'TELEGRAM onboarding step is still pending, it completes that step and advances the onboarding flow for ' +
    '(userId, planId). Use it when `edu_user_post_purchase_status` shows the user IS in the group but the onboarding ' +
    'still reports the Telegram step pending (stuck membership confirmation). Soft-degrades when TelegramBotService is ' +
    'down or membership is unknown — no-op, 200, no error. Non-destructive (only completes a step the user already ' +
    'satisfied), so it is NOT confirm-guarded. Returns `RecheckTelegramMembershipResponse { completed, status }` ' +
    '(status: member | not_member | unknown | n/a). Requires role admin or moderator.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      planId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ userId, planId }, { client }) => {
    const { data } = await client.post<unknown>(
      `/api/access/admin/users/${userId}/plans/${planId}/telegram/recheck/`,
    );
    return data;
  },
});

export const eduUserRecheckGithubMembership = defineTool({
  name: 'edu_user_recheck_github_membership',
  description:
    'Force a live GitHub-org membership check for one user and plan, then advance a pending GITHUB onboarding step ' +
    'only when the plan author GitHub App confirms the supplied login is an active org member. Intended for support ' +
    'recovery when the OAuth profile projection is stale. GitHub/API/App failures soft-degrade to `{ completed: false, ' +
    'status: "unknown" }`; a non-member returns `status: "not_member"`. Requires admin.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      planId: z.string().uuid(),
      githubLogin: z
        .string()
        .regex(/^[a-z\d](?:[a-z\d]|-(?=[a-z\d])){0,38}$/i, 'Invalid GitHub login'),
    })
    .strict(),
  handler: async ({ userId, planId, githubLogin }, { client }) => {
    const { data } = await client.post<unknown>(
      `/api/access/admin/users/${userId}/plans/${planId}/github/recheck/`,
      { githubLogin },
    );
    return data;
  },
});

export const eduGrantIssue = defineTool({
  name: 'edu_grant_issue',
  description:
    'Issue a plan grant directly to a user (AccessService admin grant — no invite link). Idempotent: if the user already ' +
    'has an ACTIVE grant on this plan, the existing grant is returned unchanged. Optional `expiresAt` (ISO-8601) sets a ' +
    'TTL — omit for a permanent grant. Publishes `plan_grant.created` → Redis access tags + onboarding + notifications + ' +
    'Telegram invite flows. Requires `plans.grant` + plan ownership (admin bypasses). Plan must not be archived. ' +
    'Destructive (grants paid access + starts downstream flows) — requires confirm = planId.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      userId: z.string().uuid(),
      expiresAt: z.string().optional(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ planId, userId, expiresAt, confirm }, { client }) => {
    requireConfirm('edu_grant_issue', planId, confirm);
    const { data } = await client.post<unknown>('/api/access/grants/admin/', {
      planId,
      userId,
      expiresAt,
    });
    return data;
  },
});

export const eduGrantRevoke = defineTool({
  name: 'edu_grant_revoke',
  description:
    'Revoke a plan grant by its grant ID (AccessService). No-op for non-ACTIVE grants. Publishes `plan_grant.revoked` → ' +
    'recalculates the user\'s Redis access tags (multi-grant overlap is handled — revoking one of two identical grants ' +
    'keeps shared access) and triggers Telegram auto-kick. Optional `reason` is recorded for audit. ' +
    'Requires `plans.grant` + plan ownership (admin bypasses). Destructive — requires confirm = grantId.',
  inputSchema: z
    .object({
      grantId: z.string().uuid(),
      reason: z.string().optional(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ grantId, reason, confirm }, { client }) => {
    requireConfirm('edu_grant_revoke', grantId, confirm);
    const { data } = await client.post<string>(`/api/access/grants/${grantId}/revoke`, {
      reason,
    });
    return { grantId: data };
  },
});

export const eduTrialCreditOverride = defineTool({
  name: 'edu_trial_credit_override',
  description:
    'Восстановить зачёт месячного доступа: разрешить пользователю доплатить до полного доступа с зачётом ' +
    'уплаченного за месяц, даже после окна. until — до какого момента действует (опц., по умолчанию +30 дней). ' +
    '(AccessService #580 — ставит `PlanGrant.CreditOverrideUntil` на trial-грант юзера; план должен быть trial, ' +
    'у юзера должен быть ACTIVE/EXPIRED grant на него. Requires `plans.grant` + plan ownership, admin bypasses. ' +
    '404 `grant.not.found` если grant'+"'"+'а нет, `plan.trial.tier_invalid` если план не trial.)',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      planId: z.string().uuid(),
      until: z.string().optional(),
    })
    .strict(),
  handler: async ({ userId, planId, until }, { client }) => {
    const { data } = await client.post<unknown>(
      `/api/access/admin/users/${userId}/trial-credit-override/`,
      { planId, until },
    );
    return data;
  },
});

export const eduInviteList = defineTool({
  name: 'edu_invite_list',
  description:
    'List invite links for a plan (AccessService), newest first. Returns `InviteLinkDto[]`: id, planId, token, createdBy, ' +
    'multiUse, maxUses, usageCount, expiresAt, isActive, label, createdAt, revokedAt. Requires `plans.manage` + plan ' +
    'ownership (admin bypasses).',
  inputSchema: z.object({ planId: z.string().uuid() }).strict(),
  handler: async ({ planId }, { client }) => {
    const { data } = await client.get<unknown[]>(`/api/access/plans/${planId}/invites/`);
    return { invites: data, count: data.length };
  },
});

export const eduInviteCreate = defineTool({
  name: 'edu_invite_create',
  description:
    'Create an invite link for a plan (AccessService) — a token a user can redeem to receive a grant. `multiUse=false` ' +
    'is single-use; `multiUse=true` with optional `maxUses` caps total redemptions (omit `maxUses` for unlimited). ' +
    'Optional `expiresAt` (ISO-8601) sets an expiry; optional `label` is a free-text note. Returns the created ' +
    '`InviteLinkDto` (including the `token`). Requires `plans.manage` + plan ownership (admin bypasses). Plan must not ' +
    'be archived. Creation is not confirm-guarded (it only mints an unredeemed link — no access granted until someone redeems).',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      multiUse: z.boolean(),
      maxUses: z.number().int().min(1).optional(),
      expiresAt: z.string().optional(),
      label: z.string().optional(),
    })
    .strict(),
  handler: async ({ planId, multiUse, maxUses, expiresAt, label }, { client }) => {
    const { data } = await client.post<unknown>(`/api/access/plans/${planId}/invites/`, {
      multiUse,
      maxUses,
      expiresAt,
      label,
    });
    return data;
  },
});

export const eduInviteRevoke = defineTool({
  name: 'edu_invite_revoke',
  description:
    'Revoke an invite link by its ID (AccessService) — sets `isActive=false` so it can no longer be redeemed. Idempotent ' +
    '(already-revoked link returns OK). Existing grants minted from past redemptions are NOT touched. Requires ' +
    '`plans.manage` + plan ownership (admin bypasses). Destructive — requires confirm = inviteId.',
  inputSchema: z
    .object({
      inviteId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ inviteId, confirm }, { client }) => {
    requireConfirm('edu_invite_revoke', inviteId, confirm);
    const { data } = await client.post<string>(`/api/access/invites/${inviteId}/revoke`);
    return { inviteId: data };
  },
});
