import { readCookieConsent } from "@/shared/lib/use-cookie-consent";
import { GROWTH_EVENT_VERSION } from "./contract";

export const ATTRIBUTION_STORAGE_KEY = "growth.v1:first-touch";
export const GROWTH_UTM_TAXONOMY = {
  utm_source: [
    "yandex",
    "google",
    "youtube",
    "telegram",
    "github",
    "habr",
    "vc",
    "dzen",
    "linkedin",
    "chatgpt",
    "direct",
    "email",
    "partner",
  ],
  utm_medium: ["organic", "cpc", "paid", "social", "referral", "email", "video", "ai", "partner"],
  utm_campaign: ["launch", "evergreen", "csharp", "dotnet", "aspnetcore"],
  utm_term: ["csharp", "dotnet", "aspnetcore", "backend", "fullstack"],
  utm_content: [
    "hero",
    "header",
    "footer",
    "course",
    "material",
    "pricing",
    "level_test",
    "youtube_description",
    "telegram_post",
  ],
} as const;

const MAX_REFERRER_HOST_LENGTH = 253;
const STATIC_LANDING_PATHS = new Set([
  "/",
  "/courses",
  "/knowledge-base",
  "/pricing",
  "/level-test",
  "/c-sharp",
  "/dotnet",
  "/asp-net-core",
]);

export type GrowthUtmKey = keyof typeof GROWTH_UTM_TAXONOMY;
export type GrowthUtmValue<Key extends GrowthUtmKey> = (typeof GROWTH_UTM_TAXONOMY)[Key][number];

interface GrowthUtmAttribution {
  utm_source?: GrowthUtmValue<"utm_source">;
  utm_medium?: GrowthUtmValue<"utm_medium">;
  utm_campaign?: GrowthUtmValue<"utm_campaign">;
  utm_term?: GrowthUtmValue<"utm_term">;
  utm_content?: GrowthUtmValue<"utm_content">;
}

export interface FirstTouchAttribution extends GrowthUtmAttribution {
  version: typeof GROWTH_EVENT_VERSION;
  landing_path: string;
  referrer_host?: string;
  captured_at: string;
}

interface StoredAttributionCandidate {
  version?: unknown;
  landing_path?: unknown;
  referrer_host?: unknown;
  captured_at?: unknown;
  utm_source?: unknown;
  utm_medium?: unknown;
  utm_campaign?: unknown;
  utm_term?: unknown;
  utm_content?: unknown;
}

function boundedString(value: unknown, maxLength: number): string | undefined {
  if (typeof value !== "string" || value.length === 0) return undefined;
  return value.slice(0, maxLength);
}

function sanitizeCampaignToken<Key extends GrowthUtmKey>(
  key: Key,
  value: unknown,
): GrowthUtmValue<Key> | undefined {
  if (typeof value !== "string") return undefined;
  const allowedValues = GROWTH_UTM_TAXONOMY[key] as readonly string[];
  return allowedValues.includes(value) ? (value as GrowthUtmValue<Key>) : undefined;
}

function sanitizeUtmFields(candidate: StoredAttributionCandidate): GrowthUtmAttribution {
  const fields: GrowthUtmAttribution = {};
  const source = sanitizeCampaignToken("utm_source", candidate.utm_source);
  const medium = sanitizeCampaignToken("utm_medium", candidate.utm_medium);
  const campaign = sanitizeCampaignToken("utm_campaign", candidate.utm_campaign);
  const term = sanitizeCampaignToken("utm_term", candidate.utm_term);
  const content = sanitizeCampaignToken("utm_content", candidate.utm_content);

  if (source) fields.utm_source = source;
  if (medium) fields.utm_medium = medium;
  if (campaign) fields.utm_campaign = campaign;
  if (term) fields.utm_term = term;
  if (content) fields.utm_content = content;
  return fields;
}

function normalizeLandingPath(value: unknown): string | null {
  if (typeof value !== "string" || !value.startsWith("/")) return null;

  const path = value.length > 1 ? value.replace(/\/+$/, "") : value;
  if (STATIC_LANDING_PATHS.has(path)) return path;

  const segments = path.split("/").filter(Boolean);
  if (segments[0] === "courses" && segments[1]) return "/courses/:slug";
  if (segments[0] === "knowledge-base" && segments[1]) return "/knowledge-base/:id";
  if (segments[0] === "pricing") return "/pricing";
  if (segments[0] === "level-test") return "/level-test";
  return "/other";
}

function parseAttribution(raw: string | null): FirstTouchAttribution | null {
  if (raw === null) return null;
  try {
    const decoded = JSON.parse(raw) as unknown;
    if (decoded === null || typeof decoded !== "object") return null;
    const parsed = decoded as StoredAttributionCandidate;
    if (parsed.version !== GROWTH_EVENT_VERSION) return null;

    const landingPath = normalizeLandingPath(parsed.landing_path);
    const capturedAt = boundedString(parsed.captured_at, 30);
    if (!landingPath || !capturedAt) return null;

    const attribution: FirstTouchAttribution = {
      version: GROWTH_EVENT_VERSION,
      landing_path: landingPath,
      captured_at: capturedAt,
      ...sanitizeUtmFields(parsed),
    };
    const referrerHost = boundedString(parsed.referrer_host, MAX_REFERRER_HOST_LENGTH);
    if (referrerHost) attribution.referrer_host = referrerHost;
    return attribution;
  } catch {
    return null;
  }
}

export function readFirstTouchAttribution(): FirstTouchAttribution | null {
  if (typeof window === "undefined" || !readCookieConsent()?.analytics) return null;
  try {
    const raw = window.localStorage.getItem(ATTRIBUTION_STORAGE_KEY);
    const attribution = parseAttribution(raw);
    if (attribution && raw !== JSON.stringify(attribution)) {
      window.localStorage.setItem(ATTRIBUTION_STORAGE_KEY, JSON.stringify(attribution));
    }
    return attribution;
  } catch {
    return null;
  }
}

function getReferrerHost(): string | undefined {
  if (!document.referrer) return undefined;
  try {
    return boundedString(
      new URL(document.referrer).hostname.toLowerCase(),
      MAX_REFERRER_HOST_LENGTH,
    );
  } catch {
    return undefined;
  }
}

export function captureFirstTouchAttribution(): FirstTouchAttribution | null {
  if (typeof window === "undefined" || typeof document === "undefined") return null;
  if (!readCookieConsent()?.analytics) return null;

  const existing = readFirstTouchAttribution();
  if (existing) return existing;

  const url = new URL(window.location.href);
  const attribution: FirstTouchAttribution = {
    version: GROWTH_EVENT_VERSION,
    landing_path: normalizeLandingPath(url.pathname) ?? "/other",
    captured_at: new Date().toISOString(),
    ...sanitizeUtmFields({
      utm_source: url.searchParams.get("utm_source"),
      utm_medium: url.searchParams.get("utm_medium"),
      utm_campaign: url.searchParams.get("utm_campaign"),
      utm_term: url.searchParams.get("utm_term"),
      utm_content: url.searchParams.get("utm_content"),
    }),
  };
  const referrerHost = getReferrerHost();
  if (referrerHost) attribution.referrer_host = referrerHost;

  try {
    window.localStorage.setItem(ATTRIBUTION_STORAGE_KEY, JSON.stringify(attribution));
  } catch {
    // Analytics must not break navigation when storage is blocked or full.
  }
  return attribution;
}
