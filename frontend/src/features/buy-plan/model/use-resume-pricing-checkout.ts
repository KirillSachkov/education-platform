"use client";

import {
  claimPricingAutoResume,
  isMatchingPricingCheckoutIntent,
  readPricingIntent,
} from "@/shared/lib/pricing-intent";
import { useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { useBuyPlan } from "./use-buy-plan";

interface ResumePublicPlan {
  id: string;
  slug: string;
}

interface UseResumePricingCheckoutOptions {
  publicPlans: readonly ResumePublicPlan[] | undefined;
  isLoading: boolean;
  isAuthenticated: boolean;
  billingEnabled: boolean;
}

export function useResumePricingCheckout({
  publicPlans,
  isLoading,
  isAuthenticated,
  billingEnabled,
}: UseResumePricingCheckoutOptions): void {
  const searchParams = useSearchParams();
  const query = searchParams.toString();
  const { buy } = useBuyPlan();

  useEffect(() => {
    if (isLoading) return;

    const currentSearchParams = new URLSearchParams(query);
    const requestedSlug = currentSearchParams.get("plan");
    const publicPlan = publicPlans?.find((plan) => plan.slug === requestedSlug);
    if (!publicPlan) return;

    document.getElementById(publicPlan.slug)?.scrollIntoView({
      behavior: "smooth",
      block: "center",
    });

    if (!isAuthenticated || !billingEnabled) return;
    const intent = readPricingIntent();
    if (!intent || !isMatchingPricingCheckoutIntent(currentSearchParams, intent)) return;
    if (!claimPricingAutoResume(intent.intentId)) return;

    buy({
      planId: publicPlan.id,
      planSlug: publicPlan.slug,
      correlationId: intent.intentId,
    });
  }, [billingEnabled, buy, isAuthenticated, isLoading, publicPlans, query]);
}
