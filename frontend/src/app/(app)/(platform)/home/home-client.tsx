"use client";

import { AuthenticatedHome, GuestHome } from "@/features/student-learning";
import { HomePinsSection } from "@/features/home-pins";
import { LevelTestPromoCard } from "@/features/level-test-runner";
import { TelegramLinkBanner } from "@/features/telegram-link";
import { useIsAuthenticated } from "@/shared/auth";
import { HomeStats } from "@/widgets/home-stats";
import { PushPermissionPrompt } from "@/widgets/push-permission-prompt";

/**
 * Client half of `/home`. Page-level server component owns the `metadata`
 * export; this component owns hooks (`useIsAuthenticated`, `useQuery`).
 *
 * Composition (top → bottom):
 *  1. AuthenticatedHome (from features/student-learning) — Continue learning,
 *     LevelStats (with leaderboard rank baked in), HomeStats (#684 — streak,
 *     activity heatmap, KPI tiles, trainer promo, passed as statsSlot),
 *     MyCourses paginated, Discussions, MaterialFeed
 *  2. PushPermissionPrompt — floating (position: fixed) PWA opt-in banner
 *
 * Access tier is intentionally NOT shown here — it already lives persistently
 * in the sidebar footer (SidebarPlanBadge); a home banner just duplicated it.
 *
 * The infinite MaterialFeed is intentionally the last in-flow block: anything
 * rendered after it is unreachable (you'd have to scroll past the whole
 * lazy-loading list). The platform-wide rank now lives inside LevelStatsCard,
 * and the inbox preview was dropped — notifications are reachable from the
 * global shell (NotificationBell in the header + the bottom-nav «Уведомления»
 * tab). PushPermissionPrompt is exempt — it's position:fixed, not in flow.
 *
 * Anonymous visitors see GuestHome. The home page is platform-wide and does
 * not resolve author metadata.
 */
export function HomeClient() {
  const isAuthenticated = useIsAuthenticated();

  return (
    <div className="mx-auto max-w-5xl px-4 sm:px-6 py-6 sm:py-10 space-y-6 sm:space-y-8">
      {isAuthenticated ? (
        <>
          <LevelTestPromoCard />
          <TelegramLinkBanner />
          <AuthenticatedHome pinsSlot={<HomePinsSection />} statsSlot={<HomeStats />} />
          <PushPermissionPrompt />
        </>
      ) : (
        <GuestHome />
      )}
    </div>
  );
}
