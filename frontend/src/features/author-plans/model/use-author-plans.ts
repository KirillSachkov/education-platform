// Barrel for backcompat — individual hooks live in their own files per
// CLAUDE.md `use-{action}-{entity}.ts` convention. Prefer importing directly
// from the per-hook file in new code.

export { useReorderPlans } from "./use-reorder-plans";
export { useCreatePlan } from "./use-create-plan";
export { useUpdatePlan } from "./use-update-plan";
export { useSetPromotion } from "./use-set-promotion";
export { useClearPromotion } from "./use-clear-promotion";
export { usePublishPlan } from "./use-publish-plan";
export { useUnpublishPlan } from "./use-unpublish-plan";
export { useArchivePlan } from "./use-archive-plan";
export { useUnarchivePlan } from "./use-unarchive-plan";
export { useDeletePlan } from "./use-delete-plan";
export { useCreateInvite } from "./use-create-invite";
export { useRevokeInvite } from "./use-revoke-invite";
export { useDeleteInvite } from "./use-delete-invite";
export { useRevokeGrant } from "./use-revoke-grant";
export { useAdminGrant } from "./use-admin-grant";

// `planQueryOptions` is a re-export of entity-level query options for legacy
// call sites — new code should import from `@/entities/access-plan` directly.
import {
  myPlansQueryOptions,
  planInvitesQueryOptions,
} from "@/entities/access-plan";

export const planQueryOptions = {
  myPlans: myPlansQueryOptions,
  invites: planInvitesQueryOptions,
};
