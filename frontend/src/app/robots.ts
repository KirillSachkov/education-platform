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
          "/saved",
          "/progress",
          "/leaderboard",
          "/trainer",
          "/level-test",
          "/roadmaps",
          "/certificates",
          "/users/",
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
