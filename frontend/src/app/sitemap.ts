import type { MetadataRoute } from "next";
import { toAbsoluteUrl } from "@/shared/config/site";

export default function sitemap(): MetadataRoute.Sitemap {
  const now = new Date();
  return [
    ...["", "offer", "privacy", "consent-pd", "cookies", "consent-marketing"].map((slug) => ({
      url: toAbsoluteUrl(slug ? `/legal/${slug}` : "/legal"),
      lastModified: now,
      changeFrequency: "monthly" as const,
      priority: 0.4,
    })),
  ];
}
