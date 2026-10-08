export const myPlansKey = ["access", "plans", "mine"] as const;
export const planInvitesKey = (planId: string) =>
  ["access", "plans", planId, "invites"] as const;
export const planGrantsKey = (planId: string) =>
  ["access", "plans", planId, "grants"] as const;
