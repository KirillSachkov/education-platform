"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";
import {
  GROWTH_CTA_PLACEMENTS,
  trackGrowthEvent,
  useTrackGrowthView,
  type GrowthCtaId,
} from "@/shared/analytics";

function isGrowthCtaId(value: string | undefined): value is GrowthCtaId {
  return value !== undefined && Object.hasOwn(GROWTH_CTA_PLACEMENTS, value);
}

export function LandingGrowthTracker() {
  const pathname = usePathname();
  useTrackGrowthView({ name: "landing_view" }, `landing:${pathname}`);

  useEffect(() => {
    const handleClick = (event: MouseEvent) => {
      if (!(event.target instanceof Element)) return;
      const target = event.target.closest<HTMLElement>("[data-growth-cta]");
      if (!target) return;

      const ctaId = target.dataset.growthCta;
      const placement = target.dataset.growthPlacement;
      if (!isGrowthCtaId(ctaId) || placement !== GROWTH_CTA_PLACEMENTS[ctaId]) return;

      trackGrowthEvent({
        name: "cta_click",
        properties: { cta_id: ctaId, placement },
      });
    };

    document.addEventListener("click", handleClick);
    return () => {
      document.removeEventListener("click", handleClick);
    };
  }, []);

  return null;
}
