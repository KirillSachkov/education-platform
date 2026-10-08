"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { useIsAuthenticated } from "@/shared/auth";
import { routes } from "@/shared/config/routes";

type EnrollmentCardState = "preview_paid" | "preview_trial" | "enrolled";

interface EnrollCardProps {
  courseId: string;
  hasFreeContent?: boolean;
  state: EnrollmentCardState;
  startHref?: string | null;
}

export function EnrollCard({ state, startHref }: EnrollCardProps) {
  const isAuthenticated = useIsAuthenticated();
  const pathname = usePathname();
  const pricingHref = routes.pricing;

  const loginUrl = `${routes.login}?callbackUrl=${encodeURIComponent(pathname)}`;

  return (
    <Card className="gap-0 border-border/60 bg-card px-4 py-4 shadow-sm sm:px-5 sm:py-5">
      <div className="flex flex-col gap-3">
        {state === "enrolled" && startHref ? (
          <Button asChild size="default" className="w-full whitespace-normal h-auto min-h-9 py-2 text-center leading-snug">
            <Link href={startHref}>Начать обучение</Link>
          </Button>
        ) : !isAuthenticated ? (
          <Button asChild size="default" className="w-full whitespace-normal h-auto min-h-9 py-2 text-center leading-snug">
            <Link href={loginUrl}>Войти, чтобы получить доступ</Link>
          </Button>
        ) : (
          // Authenticated, нет доступа: уводим на pricing.
          // Issue #358: бесплатные материалы открыты любому залогиненному (AccessType.REGISTERED),
          // отдельного claim-free flow больше нет — на /pricing только платные тарифы.
          <Button asChild size="default" className="w-full whitespace-normal h-auto min-h-9 py-2 text-center leading-snug">
            <Link href={pricingHref}>Выбрать план</Link>
          </Button>
        )}
      </div>
    </Card>
  );
}
