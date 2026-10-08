import { z } from 'zod';
import { defineTool } from '../tool.js';

export const eduUserTelegramLink = defineTool({
  name: 'edu_user_telegram_link',
  description:
    'Look up the Telegram account link for one platform user (TelegramBotService, support, #444). Resolves the ' +
    'UserLink by platform `userId` and reports whether the user has connected Telegram at all. Returns ' +
    '`AdminTelegramLinkDto { linked, telegramUserId, telegramUsername, linkedAt }` — when the user never linked, ' +
    '`{ linked: false, telegramUserId: null, telegramUsername: null, linkedAt: null }` (200, not 404). Read-only; ' +
    'the first thing to check for a "did not get into the Telegram group" report — if `linked` is false the user ' +
    'simply never connected Telegram. Requires role admin or moderator.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ userId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/telegram/admin/users/${userId}/link/`);
    return data;
  },
});

export const eduPlanTelegramChats = defineTool({
  name: 'edu_plan_telegram_chats',
  description:
    'List the Telegram chat-bindings of a plan (TelegramBotService, support, #444) — the groups/channels the bot ' +
    'invites grant-holders into. Returns `AdminPlanChatsDto { chats: AdminPlanChatDto[] }` where each chat is ' +
    '`{ telegramChatId, chatTitle, chatType, inviteLink, enrollmentGrantsMembership }`. `enrollmentGrantsMembership` ' +
    'flags the chat(s) a grant actually opens membership to (vs. informational-only bindings). Empty plan → ' +
    '`{ chats: [] }` (200). Read-only; use it to confirm a plan even has a bound chat before chasing a missing ' +
    'invite. Requires role admin or moderator.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ planId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/telegram/admin/plans/${planId}/chats/`);
    return data;
  },
});

export const eduUserResendPlanWelcome = defineTool({
  name: 'edu_user_resend_plan_welcome',
  description:
    'Re-send the Telegram welcome DM of a plan to one user (TelegramBotService, support action, #444). Forces a ' +
    'fresh send even if the welcome was already delivered once (skips the dedup check), so use it when a user says ' +
    'they bought access but never saw the onboarding message. Resolves the UserLink and a bound chat whose grant ' +
    'opens membership, then sends the configured welcome as a direct message. Safe + idempotent-ish (it only DMs ' +
    'the welcome text — no access is granted/revoked), so it is NOT confirm-guarded. Returns ' +
    '`ResendWelcomeResponse { outcome }` where outcome is one of Sent | AlreadySent | NoWelcomeConfigured | ' +
    'SendFailed | NotLinked | NoChatBound (NotLinked = user never connected Telegram; NoChatBound = plan has no ' +
    'membership-granting chat; NoWelcomeConfigured = plan has no welcome message set). Requires role admin or moderator.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
      planId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ userId, planId }, { client }) => {
    const { data } = await client.post<unknown>(
      `/api/telegram/admin/users/${userId}/plans/${planId}/welcome/resend/`,
    );
    return data;
  },
});

export const eduUserResendTelegramInvites = defineTool({
  name: 'edu_user_resend_telegram_invites',
  description:
    'Re-issue Telegram group invite DMs to one user across all plans they hold (TelegramBotService, support action, ' +
    '#444) — admin variant of the user-facing "resync my invites". Resolves the UserLink and re-runs the same invite ' +
    'resync used by the self endpoint, re-sending invite links for every membership-granting chat the user is ' +
    'entitled to but not yet in. Use it for the "paid for a course but did not get the Telegram invite" case. ' +
    'Non-destructive (only sends invite DMs), so it is NOT confirm-guarded. Returns ' +
    '`ResyncInvitesResponse { invitesSent, telegramLinked }` — when the user never connected Telegram, ' +
    '`{ invitesSent: 0, telegramLinked: false }`. Requires role admin or moderator.',
  inputSchema: z
    .object({
      userId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ userId }, { client }) => {
    const { data } = await client.post<unknown>(
      `/api/telegram/admin/users/${userId}/resync-invites/`,
    );
    return data;
  },
});
