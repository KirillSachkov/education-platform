export const ACCESS_TYPES = ["PUBLIC", "REGISTERED", "ENROLLED"] as const;

export type AccessType = (typeof ACCESS_TYPES)[number];
