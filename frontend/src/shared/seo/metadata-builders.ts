import type { Metadata } from "next";
import { APP_URL } from "@/shared/config/site";

const SITE_NAME = "SachkovLearn";
const LOCALE = "ru_RU";
const DEFAULT_OG_IMAGE = "/og-image.png";
const OG_IMAGE_WIDTH = 1200;
const OG_IMAGE_HEIGHT = 630;

const apiUrl =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

export interface OgEntityInput {
  title: string;
  description?: string | null;
  imageUrl?: string | null;
  path: string;
  type?: "article" | "website";
  fullTitle?: string;
}

export function buildEntityMetadata({
  title,
  description,
  imageUrl,
  path,
  type = "article",
  fullTitle,
}: OgEntityInput): Metadata {
  const cleanedDescription = description ? truncate(stripMarkdown(description), 160) : undefined;
  const ogTitle = fullTitle ?? title;
  const url = absoluteUrl(path);
  // Real cover wins; cover-less entities fall back to a dynamically generated
  // titled card (`/og`) instead of the generic static image.
  const image = imageUrl
    ? absoluteUrl(imageUrl)
    : buildFallbackOgImageUrl(title, cleanedDescription);

  return {
    title,
    description: cleanedDescription,
    openGraph: {
      title: ogTitle,
      description: cleanedDescription,
      type,
      url,
      siteName: SITE_NAME,
      locale: LOCALE,
      images: [{ url: image, width: OG_IMAGE_WIDTH, height: OG_IMAGE_HEIGHT, alt: title }],
    },
    twitter: {
      card: "summary_large_image",
      title: ogTitle,
      description: cleanedDescription,
      images: [image],
    },
    alternates: {
      canonical: url,
    },
  };
}

/**
 * Builds a `/og` dynamic-card URL for entities without a cover image, so the
 * shared link still renders as a titled card rather than the generic static
 * `/og-image.png`. The card generation lives in `app/og/route.tsx`.
 */
export function buildFallbackOgImageUrl(title: string, subtitle?: string | null): string {
  const params = new URLSearchParams({ title });
  if (subtitle) params.set("subtitle", subtitle);
  return absoluteUrl(`/og?${params.toString()}`);
}

export function absoluteUrl(value: string | null | undefined): string {
  if (!value) return `${APP_URL}${DEFAULT_OG_IMAGE}`;
  if (/^https?:\/\//i.test(value)) return value;
  const base = APP_URL.endsWith("/") ? APP_URL.slice(0, -1) : APP_URL;
  return `${base}${value.startsWith("/") ? value : `/${value}`}`;
}

export function truncate(value: string, max: number): string {
  if (value.length <= max) return value;
  return `${value.slice(0, max - 1).trimEnd()}…`;
}

export function stripMarkdown(value: string): string {
  return value
    .replace(/```[\s\S]*?```/g, " ")
    .replace(/`[^`]*`/g, " ")
    .replace(/!\[[^\]]*\]\([^)]*\)/g, " ")
    .replace(/\[([^\]]*)\]\([^)]*\)/g, "$1")
    .replace(/^>+\s?/gm, "")
    .replace(/^#+\s+/gm, "")
    .replace(/[*_~]+/g, "")
    .replace(/\s+/g, " ")
    .trim();
}

type Envelope<T> = { result?: T | null; isError?: boolean };

export async function fetchAnonymous<T>(
  path: string,
  options: { cache?: RequestCache } = {},
): Promise<T | null> {
  try {
    const res = await fetch(
      `${apiUrl}${path}`,
      options.cache === "no-store" ? { cache: "no-store" } : { next: { revalidate: 300 } },
    );
    if (!res.ok) return null;
    const data = (await res.json()) as Envelope<T>;
    return data.result ?? null;
  } catch {
    return null;
  }
}
