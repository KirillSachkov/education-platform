/** Read and clear legacy checkout context only for already-created payment settlement. */
export const PRICING_INTENT_STORAGE_KEY = "growth.pricingIntent.v1";
export const PRICING_INTENT_TTL_MS = 30 * 60 * 1000;

const PLAN_SLUG_MAX_LENGTH = 80;
const PLAN_SLUG_PATTERN = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const ISO_TIMESTAMP_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/;
const INTENT_KEYS = ["createdAt", "intentId", "planSlug"] as const;
const AUTO_RESUME_KEY_PREFIX = `${PRICING_INTENT_STORAGE_KEY}.autoResume:`;

export interface PricingIntent {
  intentId: string;
  planSlug: string;
  createdAt: string;
}

function isValidPlanSlug(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.length <= PLAN_SLUG_MAX_LENGTH &&
    PLAN_SLUG_PATTERN.test(value)
  );
}

function isCanonicalUuid(value: unknown): value is string {
  return typeof value === "string" && UUID_PATTERN.test(value);
}

export function parsePricingIntent(raw: string | null, now = Date.now()): PricingIntent | null {
  if (!raw || !Number.isFinite(now)) return null;

  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return null;
  }

  if (typeof value !== "object" || value === null || Array.isArray(value)) return null;
  const record = value as Record<string, unknown>;
  const keys = Object.keys(record).sort();
  if (keys.length !== INTENT_KEYS.length || keys.some((key, index) => key !== INTENT_KEYS[index])) {
    return null;
  }
  if (!isCanonicalUuid(record.intentId) || !isValidPlanSlug(record.planSlug)) return null;
  if (typeof record.createdAt !== "string" || !ISO_TIMESTAMP_PATTERN.test(record.createdAt)) {
    return null;
  }

  const createdAtMs = Date.parse(record.createdAt);
  if (!Number.isFinite(createdAtMs) || new Date(createdAtMs).toISOString() !== record.createdAt) {
    return null;
  }
  const age = now - createdAtMs;
  if (age < 0 || age > PRICING_INTENT_TTL_MS) return null;

  return {
    intentId: record.intentId,
    planSlug: record.planSlug,
    createdAt: record.createdAt,
  };
}

export function readPricingIntent(now = Date.now()): PricingIntent | null {
  try {
    return parsePricingIntent(sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY), now);
  } catch {
    return null;
  }
}

export function clearPricingIntent(intentId: string): void {
  if (!isCanonicalUuid(intentId)) return;
  try {
    const raw = sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY);
    const stored: unknown = raw ? JSON.parse(raw) : null;
    if (
      typeof stored === "object" &&
      stored !== null &&
      !Array.isArray(stored) &&
      (stored as Record<string, unknown>).intentId === intentId
    ) {
      sessionStorage.removeItem(PRICING_INTENT_STORAGE_KEY);
      sessionStorage.removeItem(`${AUTO_RESUME_KEY_PREFIX}${intentId}`);
    }
  } catch {
    // Browser storage can be unavailable in privacy mode.
  }
}
