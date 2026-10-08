import { LandingHeader } from "@/features/landing-header";
import { LandingGrowthTracker } from "@/features/course-landing/pages/platform";
import { routes } from "@/shared/config/routes";

/**
 * Layout for the keyword SEO landings (`/c-sharp`, `/dotnet`, `/asp-net-core`).
 *
 * Mirrors the marketing landing's dark scope so the brand reads consistently,
 * but without the homepage's section-anchor nav (those `#program`-style anchors
 * only exist on `/`). Just the logo + a CTA to the platform.
 */
export default function SeoLandingLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="dark [color-scheme:dark] min-h-svh flex flex-col bg-[#0A0A0B]">
      <LandingGrowthTracker />
      <LandingHeader ctaText="Перейти на платформу" ctaHref={routes.home} />
      <main id="main-content" className="-mt-14 flex-1">
        {children}
      </main>
    </div>
  );
}
