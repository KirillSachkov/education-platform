import type { MetadataRoute } from "next";
import { toAbsoluteUrl } from "@/shared/config/site";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      {
        userAgent: "*",
        allow: "/",
        disallow: [
          "/admin/",
          "/onboarding/",
          "/api/",
          "/internal/",
          "/auth/",
          "/connect/",
          "/.well-known/",
          "/author/",
          "/profile",
          "/settings/",
          "/notifications",
          "/home",
          // NB: NOT "/courses" — that path is the PUBLIC catalog + course landing
          // pages (primary commercial SEO targets, advertised in sitemap.ts). It is
          // also `routes.myCourses`, but the authenticated "Мои курсы" view shares
          // the path and renders the public catalog for crawlers. Disallowing it
          // deindexes every course page (regression: added in #283, fixed in #449).
          "/n/",
          "/bot-link/",
          "/invite/",
          "/auth-error",
          // Internal Next.js route-group target — public-facing URL is `/@slug`
          "/spaces/",
        ],
      },
    ],
    sitemap: toAbsoluteUrl("/sitemap.xml"),
  };
}
