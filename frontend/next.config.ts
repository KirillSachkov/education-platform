import withBundleAnalyzer from "@next/bundle-analyzer";
import type { NextConfig } from "next";
import path from "node:path";
import { fileURLToPath } from "node:url";

const projectRoot = path.dirname(fileURLToPath(import.meta.url));

function joinCspSources(...sources: Array<string | undefined | false>) {
  return sources.filter(Boolean).join(" ");
}

const nextConfig: NextConfig = {
  reactCompiler: true,
  reactStrictMode: true,
  poweredByHeader: false,
  output: "standalone",
  // Repo root also has a package.json (husky/lint-staged/prettier). Without
  // this Turbopack walks up, picks the monorepo root, and starts treating
  // frontend sources as CommonJS → "module format mismatch" on every file.
  turbopack: {
    root: projectRoot,
  },
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "images.unsplash.com",
      },
      {
        protocol: "https",
        hostname: "kinescope.io",
      },
      {
        protocol: "https",
        hostname: "*.kinescope.io",
      },
      {
        protocol: "https",
        hostname: "kinescope.com",
      },
      {
        protocol: "https",
        hostname: "*.kinescope.com",
      },
      {
        protocol: "https",
        hostname: "kinescopecdn.net",
      },
      {
        protocol: "https",
        hostname: "*.storage.yandexcloud.net",
      },
      {
        protocol: "https",
        hostname: "avatars.githubusercontent.com",
      },
    ],
  },
  async redirects() {
    return [
      // Phase 5 (#308) — single-tenant URL flattening. Legacy /@slug/* paths
      // permanently 308-redirect to flat /<path>. /@slug or /@slug/home → /home.
      { source: "/@:slug", destination: "/home", permanent: true },
      { source: "/@:slug/home", destination: "/home", permanent: true },
      { source: "/@:slug/:path*", destination: "/:path*", permanent: true },
      // #414 — «Платежи» и «Планы» промоутнуты из settings в top-level sidebar.
      // Старые URL 308-редиректим на новые дедик-страницы.
      { source: "/settings/payments", destination: "/payments", permanent: true },
      { source: "/settings/plans", destination: "/my-plans", permanent: true },
      // #586 — флэт /learn/{id} (standalone-материал) переехал на /knowledge-base/{id}
      // при unification материалов / Phase 5. Мост для уже разосланных ссылок:
      // short-link'и (#507), дайджест-письма (WeeklyDigestRunner), home-pins.
      // In-course путь /courses/{slug}/learn/{id} не задевается (другой префикс).
      { source: "/learn/:materialId", destination: "/knowledge-base/:materialId", permanent: true },
    ];
  },
  async headers() {
    // CSP state (verified 2026-05-23 на Next.js 16.2.6 + Turbopack):
    //
    // 1. `script-src 'unsafe-inline' 'unsafe-eval'` оставлены ТРЕМЯ причинами:
    //    a) Kinescope player делает eval в runtime (verified в #193).
    //    b) Next.js inject'ит inline scripts для router hydration / RSC payload.
    //    c) Nonce-based CSP требует proxy.ts + переход всех страниц в
    //       dynamic rendering (по Next.js 16 docs:
    //       https://nextjs.org/docs/app/guides/content-security-policy).
    //       Цена — потеря static optimization на landing/legal/catalog,
    //       CDN cache miss, SEO regression. Не оправдано пока security
    //       не упирается в это.
    //
    // 2. proxy.ts build verified — добавление пустого proxy.ts с output:"standalone"
    //    + Turbopack collects clean (#306). Lazy-комментарий про
    //    "middleware.js.nft.json bug" был cargo-cult, удалён.
    //
    // 3. Defense-in-depth: experimental.sri (Subresource Integrity) можно
    //    включить как опциональный hash-check на script bundles без
    //    breaking static rendering. Tracking — issue #306.
    //
    // 4. Trusted Types: добавлен Content-Security-Policy-Report-Only с
    //    `require-trusted-types-for 'script'` (см. ниже). Violations
    //    логируются в browser console / DevTools Issues. Report-collector
    //    endpoint — future work (TODO в #306).
    return [
      {
        source: "/:path*",
        headers: [
          { key: "X-Frame-Options", value: "DENY" },
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          {
            // Permissions-Policy — закрываем все browser features которые мы не
            // используем. clipboard-write=(self) потому что есть copy buttons
            // (author-plan-detail invite link, markdown-code copy). fullscreen
            // ограничен self + kinescope iframe. interest-cohort не добавляем —
            // FLoC deprecated, заменён browsing-topics.
            key: "Permissions-Policy",
            value: [
              "accelerometer=()",
              "bluetooth=()",
              "browsing-topics=()",
              "camera=()",
              "clipboard-write=(self)",
              "display-capture=()",
              'fullscreen=(self "https://kinescope.io" "https://*.kinescope.io")',
              "geolocation=()",
              "gyroscope=()",
              "idle-detection=()",
              "magnetometer=()",
              "microphone=()",
              "payment=()",
              "serial=()",
              "usb=()",
            ].join(", "),
          },
          {
            // HSTS preload: после submit на https://hstspreload.org cache
            // живёт ~1 год в браузерах. Submit делает владелец вручную
            // только после verify что все subdomain работают по HTTPS.
            key: "Strict-Transport-Security",
            value: "max-age=31536000; includeSubDomains; preload",
          },
          {
            // COOP `same-origin-allow-popups` изолирует browsing context (защита
            // от cross-origin window.opener-атак типа tabnabbing), но **позволяет**
            // OIDC-popup flow (window.opener доступен на popup'е, который сами
            // открыли). Без `-allow-popups` сломались бы popup'ы платежных
            // провайдеров / OAuth.
            //
            // COEP (`require-corp`) умышленно НЕ выставляем — Kinescope iframe
            // не отдаёт CORP-header, страница с видео сломалась бы. Если когда-
            // нибудь Kinescope добавит CORP — включаем `require-corp` + получаем
            // SharedArrayBuffer / cross-origin-isolated. См. issue #293.
            key: "Cross-Origin-Opener-Policy",
            value: "same-origin-allow-popups",
          },
          {
            key: "Content-Security-Policy",
            value: [
              "default-src 'self'",
              "base-uri 'self'",
              "form-action 'self'",
              "object-src 'none'",
              `script-src ${joinCspSources(
                "'self'",
                "'unsafe-inline'",
                "'unsafe-eval'",
                "https://player.kinescope.io",
              )}`,
              "style-src 'self' 'unsafe-inline'",
              `img-src ${joinCspSources(
                "'self'",
                "data:",
                "blob:",
                "https://images.unsplash.com",
                "https://kinescope.io",
                "https://*.kinescope.io",
                "https://kinescope.com",
                "https://*.kinescope.com",
                "https://kinescopecdn.net",
                "https://storage.yandexcloud.net",
                "https://*.storage.yandexcloud.net",
                "https://avatars.githubusercontent.com",
                process.env.NEXT_PUBLIC_AUTH_ORIGIN,
              )}`,
              "font-src 'self' data:",
              `connect-src ${joinCspSources(
                "'self'",
                process.env.NEXT_PUBLIC_API_URL,
                process.env.NEXT_PUBLIC_AUTH_ORIGIN,
                "https://kinescope.io",
                "https://*.kinescope.io",
                "https://kinescope.com",
                "https://*.kinescope.com",
                "https://storage.yandexcloud.net",
                "https://*.storage.yandexcloud.net",
              )}`,
              "frame-src 'self' https://kinescope.io https://*.kinescope.io https://kinescope.com https://*.kinescope.com",
              "media-src 'self' https://kinescope.io https://*.kinescope.io https://kinescope.com https://*.kinescope.com https://kinescopecdn.net",
              // Modern equivalent of X-Frame-Options: DENY. Both headers
              // remain: X-Frame-Options для legacy браузеров, frame-ancestors
              // для современных (modern wins при наличии обоих).
              "frame-ancestors 'none'",
            ].join("; "),
          },
          // (#306) Report-only Trusted Types CSP убран: без report-uri он ничего
          // не enforce'ил и не репортил, только спамил console сотнями
          // require-trusted-types-for violations из Next/Turbopack-тулинга.
        ],
      },
    ];
  },
};

export default withBundleAnalyzer({ enabled: process.env.ANALYZE === "true" })(nextConfig);
