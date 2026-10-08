import { sanitizeCallbackUrl } from "./sanitize-callback-url";

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

export interface PricingCheckoutSearchParams {
  get(name: string): string | null;
  entries(): IterableIterator<[string, string]>;
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

export function createPricingIntent(planSlug: string, now = Date.now()): PricingIntent | null {
  if (!isValidPlanSlug(planSlug) || !Number.isFinite(now)) return null;
  return {
    intentId: crypto.randomUUID(),
    planSlug,
    createdAt: new Date(now).toISOString(),
  };
}

export function buildPricingCheckoutCallback(intent: PricingIntent): string {
  const query = new URLSearchParams({
    plan: intent.planSlug,
    intent: intent.intentId,
    resume: "checkout",
  });
  return sanitizeCallbackUrl(`/pricing?${query.toString()}`, "/pricing");
}

export function isMatchingPricingCheckoutIntent(
  searchParams: PricingCheckoutSearchParams,
  intent: PricingIntent,
): boolean {
  const entries = Array.from(searchParams.entries());
  if (entries.length !== 3 || new Set(entries.map(([key]) => key)).size !== 3) return false;
  return (
    searchParams.get("plan") === intent.planSlug &&
    searchParams.get("intent") === intent.intentId &&
    searchParams.get("resume") === "checkout"
  );
}

export function readPricingIntentForCallback(
  callbackUrl: string,
  now = Date.now(),
): PricingIntent | null {
  const safeCallback = sanitizeCallbackUrl(callbackUrl, "");
  if (!safeCallback) return null;

  const url = new URL(safeCallback, "https://pricing-intent.invalid");
  if (url.pathname !== "/pricing" || url.hash) return null;

  const intent = readPricingIntent(now);
  return intent && isMatchingPricingCheckoutIntent(url.searchParams, intent) ? intent : null;
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

export function storePricingIntent(planSlug: string, now = Date.now()): PricingIntent | null {
  const intent = createPricingIntent(planSlug, now);
  if (!intent) return null;
  try {
    sessionStorage.setItem(PRICING_INTENT_STORAGE_KEY, JSON.stringify(intent));
    return intent;
  } catch {
    return null;
  }
}

export function readPricingIntent(now = Date.now()): PricingIntent | null {
  try {
    return parsePricingIntent(sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY), now);
  } catch {
    return null;
  }
}

export function claimPricingAutoResume(intentId: string): boolean {
  if (!isCanonicalUuid(intentId)) return false;
  const key = `${AUTO_RESUME_KEY_PREFIX}${intentId}`;
  try {
    if (sessionStorage.getItem(key) === "1") return false;
    sessionStorage.setItem(key, "1");
    return true;
  } catch {
    return false;
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
