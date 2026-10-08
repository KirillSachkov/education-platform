import type { MetadataRoute } from "next";
import { routes } from "@/shared/config/routes";
import { APP_URL, toAbsoluteUrl } from "@/shared/config/site";

const apiUrl =
  process.env.API_URL_INTERNAL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost/api";

// Render at request time, not at `next build`. The course/plan URLs below are
// fetched from the API, which is unreachable during the Docker build (`apiUrl`
// falls back to localhost) — a statically prerendered sitemap therefore bakes in
// only the static + legal routes and silently drops every course/plan page
// (regression confirmed on prod: sitemap.xml had no /courses/<slug>). Forcing
// dynamic rendering moves generation into the running container where
// API_URL_INTERNAL resolves, so individual course + plan pages are always
// included. The fetches keep their own `revalidate` so this stays cheap.
export const dynamic = "force-dynamic";

// Slugs of publishable legal documents — must stay in sync with
// `frontend/src/shared/legal/versions.ts` (which lives in feature/legal-base-49 MR
// and may not yet be merged when this MR lands). Hardcoded here on purpose so
// sitemap doesn't import a not-yet-existing module.
const LEGAL_SLUGS = ["offer", "privacy", "consent-pd", "cookies", "consent-marketing"];

interface CourseSitemapItem {
  id: string;
  slug: string;
}

interface TimestampedSitemapItem {
  id: string;
  updatedAt: string;
}

interface RoadmapSitemapItem {
  slug: string;
  updatedAt: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isCourseSitemapItem(value: unknown): value is CourseSitemapItem {
  return isRecord(value) && typeof value.id === "string" && typeof value.slug === "string";
}

function isTimestampedSitemapItem(value: unknown): value is TimestampedSitemapItem {
  return isRecord(value) && typeof value.id === "string" && typeof value.updatedAt === "string";
}

function isRoadmapSitemapItem(value: unknown): value is RoadmapSitemapItem {
  return isRecord(value) && typeof value.slug === "string" && typeof value.updatedAt === "string";
}

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const now = new Date();

  const staticRoutes: MetadataRoute.Sitemap = [
    // Marketing landing — top priority for crawlers entering via google.com/etc.
    // Listed once at the canonical (no trailing slash) URL to match <link rel="canonical">.
    // `/login` is intentionally omitted: it is `noindex,nofollow`, so it must not be in the sitemap.
    { url: APP_URL, lastModified: now, changeFrequency: "weekly", priority: 1 },
    {
      url: toAbsoluteUrl(routes.pricing),
      lastModified: now,
      changeFrequency: "weekly",
      priority: 0.9,
    },
    // Level-test funnel landing (#482) — высокий приоритет: точка входа лид-воронки.
    {
      url: toAbsoluteUrl(routes.levelTest),
      lastModified: now,
      changeFrequency: "weekly",
      priority: 0.9,
    },
    {
      url: toAbsoluteUrl(routes.courses),
      lastModified: now,
      changeFrequency: "daily",
      priority: 0.8,
    },
    // Keyword SEO landings (#530) — intent-split entry pages for «c# курс /
    // .net обучение / asp.net core курс». Commercial search-entry → high priority.
    {
      url: toAbsoluteUrl(routes.seoCsharp),
      lastModified: now,
      changeFrequency: "monthly",
      priority: 0.8,
    },
    {
      url: toAbsoluteUrl(routes.seoDotnet),
      lastModified: now,
      changeFrequency: "monthly",
      priority: 0.8,
    },
    {
      url: toAbsoluteUrl(routes.seoAspNetCore),
      lastModified: now,
      changeFrequency: "monthly",
      priority: 0.8,
    },
    {
      url: toAbsoluteUrl(routes.leaderboard),
      lastModified: now,
      changeFrequency: "daily",
      priority: 0.6,
    },
    {
      url: toAbsoluteUrl(routes.knowledgeBase),
      lastModified: now,
      changeFrequency: "daily",
      priority: 0.6,
    },
    // `/collections` redirects to `/knowledge-base`; only concrete
    // `/collections/{id}` detail pages belong in the sitemap.
    // `/roadmaps` is intentionally omitted: there is no index page (only `/roadmaps/[slug]`),
    // so the bare route 404s — advertising it in the sitemap triggers Search Console errors (#386).
  ];

  const legalRoutes: MetadataRoute.Sitemap = [
    {
      url: toAbsoluteUrl("/legal"),
      lastModified: now,
      changeFrequency: "monthly",
      priority: 0.4,
    },
    ...LEGAL_SLUGS.map((slug) => ({
      url: toAbsoluteUrl(`/legal/${slug}`),
      lastModified: now,
      changeFrequency: "monthly" as const,
      priority: 0.5,
    })),
  ];

  let courseRoutes: MetadataRoute.Sitemap = [];
  try {
    const res = await fetch(`${apiUrl}/courses/catalog/?limit=100`, {
      next: { revalidate: 3600 },
      signal: AbortSignal.timeout(5_000),
    });
    if (res.ok) {
      const data: unknown = await res.json();
      const result = isRecord(data) && isRecord(data.result) ? data.result : null;
      const courses = (result && Array.isArray(result.items) ? result.items : []).filter(
        isCourseSitemapItem,
      );
      courseRoutes = courses.map((course) => ({
        url: toAbsoluteUrl(routes.courseOverview(course.slug)),
        lastModified: now,
        changeFrequency: "weekly",
        priority: 0.7,
      }));
    }
  } catch {
    // API unavailable during build — sitemap still usable, just lacks course URLs.
  }

  // Knowledge base: only public PUBLISHED material/collection/roadmap entries returned by
  // the public-only export contract. Restricted content must never become a crawler target.
  let knowledgeBaseRoutes: MetadataRoute.Sitemap = [];
  try {
    const res = await fetch(`${apiUrl}/materials/sitemap-export/`, {
      next: { revalidate: 3600 },
      signal: AbortSignal.timeout(5_000),
    });
    if (res.ok) {
      const data: unknown = await res.json();
      const result = isRecord(data) && isRecord(data.result) ? data.result : null;
      const materials = (result && Array.isArray(result.materials) ? result.materials : []).filter(
        isTimestampedSitemapItem,
      );
      const collections = (
        result && Array.isArray(result.collections) ? result.collections : []
      ).filter(isTimestampedSitemapItem);
      const roadmaps = (result && Array.isArray(result.roadmaps) ? result.roadmaps : []).filter(
        isRoadmapSitemapItem,
      );
      knowledgeBaseRoutes = [
        ...materials.map((material) => ({
          url: toAbsoluteUrl(routes.knowledgeBaseMaterial(material.id)),
          lastModified: new Date(material.updatedAt),
          changeFrequency: "weekly" as const,
          priority: 0.6,
        })),
        ...collections.map((collection) => ({
          url: toAbsoluteUrl(routes.collectionDetail(collection.id)),
          lastModified: new Date(collection.updatedAt),
          changeFrequency: "weekly" as const,
          priority: 0.6,
        })),
        ...roadmaps.map((roadmap) => ({
          url: toAbsoluteUrl(routes.publicRoadmap(roadmap.slug)),
          lastModified: new Date(roadmap.updatedAt),
          changeFrequency: "monthly" as const,
          priority: 0.6,
        })),
      ];
    }
  } catch {
    // API unavailable — sitemap still usable, just lacks knowledge-base URLs.
  }

  return [...staticRoutes, ...legalRoutes, ...courseRoutes, ...knowledgeBaseRoutes];
}
