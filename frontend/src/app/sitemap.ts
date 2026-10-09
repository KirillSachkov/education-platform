import type { MetadataRoute } from "next";
import { routes } from "@/shared/config/routes";
import { APP_URL, toAbsoluteUrl } from "@/shared/config/site";

export default function sitemap(): MetadataRoute.Sitemap {
  const now = new Date();
  return [
    { url: APP_URL, lastModified: now, changeFrequency: "weekly", priority: 1 },
    ...[routes.pricing, routes.seoCsharp, routes.seoDotnet, routes.seoAspNetCore].map((path) => ({
      url: toAbsoluteUrl(path),
      lastModified: now,
      changeFrequency: "weekly" as const,
      priority: 0.8,
    })),
    ...["", "offer", "privacy", "consent-pd", "cookies", "consent-marketing"].map((slug) => ({
      url: toAbsoluteUrl(slug ? `/legal/${slug}` : "/legal"),
      lastModified: now,
      changeFrequency: "monthly" as const,
      priority: 0.4,
    })),
  ];
}
