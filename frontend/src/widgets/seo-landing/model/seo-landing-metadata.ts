import type { Metadata } from "next";
import { APP_URL } from "@/shared/config/site";
import type { SeoLandingContent } from "./types";

/**
 * Builds Next.js `Metadata` for a keyword SEO landing.
 *
 * `title` is a plain string so the root template ("%s | SachkovLearn") appends the
 * brand once — `metaTitle` values must NOT already include the brand. `canonical`
 * is self-referential (the page is unique segment content, never a doorway to `/`).
 */
const OG_IMAGE = { url: "/og-image.png", width: 1200, height: 630 };

export function seoLandingMetadata(content: SeoLandingContent): Metadata {
  const url = `${APP_URL}${content.slug}`;
  // OG/Twitter title intentionally omits "— SachkovLearn": `siteName` already
  // carries the brand in the preview card, and appending it pushed two of the
  // three titles past the ~70-char cut-off used by Telegram/VK/Facebook.
  return {
    title: content.metaTitle,
    description: content.metaDescription,
    alternates: { canonical: url },
    openGraph: {
      title: content.metaTitle,
      description: content.metaDescription,
      type: "website",
      url,
      siteName: "SachkovLearn",
      locale: "ru_RU",
      images: [OG_IMAGE],
    },
    twitter: {
      card: "summary_large_image",
      title: content.metaTitle,
      description: content.metaDescription,
      images: [OG_IMAGE.url],
    },
  };
}
