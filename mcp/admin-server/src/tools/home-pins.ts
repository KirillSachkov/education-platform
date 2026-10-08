import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

/**
 * Tools for managing a plan's "home pins" — materials an author pins to the
 * home dashboard of everyone who holds a grant on the plan — plus a destructive
 * bulk-reset of per-plan onboarding (epic #397). All wrap AccessService admin
 * endpoints under `/access/plans/{planId}/...` (Plans.MANAGE + ownership; admin
 * bypasses ownership). Routes go through nginx `/api/access/` which strips the
 * `/api/` prefix and forwards the rest verbatim — so the trailing-slash shape of
 * each path must match the backend route template exactly.
 *
 * The home-pin list returns titles enriched via ECS (soft-degrades to "" when
 * ECS is unreachable); the per-pin `pinId` is the handle for update/reorder/remove.
 */

interface HomePinListItem {
  pinId: string;
  materialId: string;
  title: string;
  note: string | null;
  sortKey: string;
}

export const homePinsList = defineTool({
  name: 'home_pins_list',
  description:
    'List the home pins of a plan (Plans.MANAGE + ownership). Pins are materials the author pins to the ' +
    'home dashboard shown to every grant-holder of the plan. Returns an ordered list of ' +
    '{ pinId, materialId, title, note, sortKey } (sorted by fractional sortKey). `title` is enriched via ECS ' +
    'and soft-degrades to "" if ECS is unreachable. Use the returned `pinId` as the handle for ' +
    'home_pins_update_note / home_pins_reorder / home_pins_remove. Read-only.',
  inputSchema: z.object({ planId: z.string().uuid() }).strict(),
  handler: async ({ planId }, { client }) => {
    const { data } = await client.get<HomePinListItem[]>(
      `/api/access/plans/${planId}/home-pins/`,
    );
    return { pins: data };
  },
});

export const homePinsAdd = defineTool({
  name: 'home_pins_add',
  description:
    'Pin a material to a plan\'s home dashboard (Plans.MANAGE + ownership). The pin is appended to the end of ' +
    'the list. Returns { pinId } of the created pin. ' +
    'Surfaces 404 (home_pin.material.not.found — the material does not exist) and ' +
    '409 (home_pin.already.pinned — this material is already pinned to the plan) as tool errors. ' +
    'Optional `note` (max 500 chars) is a short author annotation shown next to the pin.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      materialId: z.string().uuid(),
      note: z.string().max(500).optional(),
    })
    .strict(),
  handler: async ({ planId, materialId, note }, { client }) => {
    const { data } = await client.post<string>(`/api/access/plans/${planId}/home-pins/`, {
      materialId,
      note: note ?? null,
    });
    return { pinId: data };
  },
});

export const homePinsUpdateNote = defineTool({
  name: 'home_pins_update_note',
  description:
    "Update the author note on an existing home pin (Plans.MANAGE + ownership). Omit `note` or pass null to " +
    'clear it. Max 500 chars. Returns { pinId }. ' +
    '404 (home_pin.not.found) if the pin does not exist or belongs to a different plan.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      pinId: z.string().uuid(),
      note: z.string().max(500).nullable().optional(),
    })
    .strict(),
  handler: async ({ planId, pinId, note }, { client }) => {
    const { data } = await client.patch<string>(
      `/api/access/plans/${planId}/home-pins/${pinId}/`,
      { note: note ?? null },
    );
    return { pinId: data };
  },
});

export const homePinsReorder = defineTool({
  name: 'home_pins_reorder',
  description:
    'Reorder a home pin to a new fractional position between two neighbour pins (Plans.MANAGE + ownership). ' +
    'Pass `beforeId` and/or `afterId` (other pinIds in the same plan) to compute the new slot: ' +
    'beforeId-only = move to just after that pin (to the end if it is last); afterId-only = move to just before it; ' +
    'both = move between them; neither = move to the very start. Returns { pinId }. ' +
    '404 (home_pin.not.found) if the target pin or a referenced neighbour is not in this plan. ' +
    'Get current pinIds + ordering from home_pins_list first.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      pinId: z.string().uuid(),
      beforeId: z.string().uuid().optional(),
      afterId: z.string().uuid().optional(),
    })
    .strict(),
  handler: async ({ planId, pinId, beforeId, afterId }, { client }) => {
    const { data } = await client.post<string>(
      `/api/access/plans/${planId}/home-pins/${pinId}/order/`,
      { beforeId: beforeId ?? null, afterId: afterId ?? null },
    );
    return { pinId: data };
  },
});

export const homePinsRemove = defineTool({
  name: 'home_pins_remove',
  description:
    'Remove a home pin from a plan (Plans.MANAGE + ownership). Only un-pins from the home dashboard — the ' +
    'material itself is untouched. Returns { pinId } of the removed pin. ' +
    '404 (home_pin.not.found) if the pin does not exist or belongs to a different plan. Requires confirm = pinId.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      pinId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ planId, pinId, confirm }, { client }) => {
    requireConfirm('home_pins_remove', pinId, confirm);
    const { data } = await client.delete<string>(
      `/api/access/plans/${planId}/home-pins/${pinId}/`,
    );
    return { pinId: data };
  },
});

interface ResetAllOnboardingsResponse {
  resetCount: number;
}

export const onboardingResetAll = defineTool({
  name: 'onboarding_reset_all',
  description:
    'DESTRUCTIVE: re-run the onboarding wizard for ALL grant-holders of a plan (Plans.MANAGE + ownership). ' +
    'Every existing onboarding row is reset to the first step (completed/skipped steps cleared, completion nulled), ' +
    'so every student who already finished onboarding will be forced through it again — use only after a ' +
    'meaningful change to the onboarding flow. If the flow is disabled or has no steps, resets nothing and returns ' +
    'resetCount = 0 (avoids stranding users in a gate-loop). Resets commit in chunks of 200, so a large plan ' +
    'does not hold one giant transaction. Returns { resetCount } — the number of grant-holders re-onboarded. ' +
    'Requires confirm = planId.',
  inputSchema: z
    .object({
      planId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ planId, confirm }, { client }) => {
    requireConfirm('onboarding_reset_all', planId, confirm);
    const { data } = await client.post<ResetAllOnboardingsResponse>(
      `/api/access/plans/${planId}/onboarding-flow/reset-all/`,
    );
    return { resetCount: data.resetCount };
  },
});
