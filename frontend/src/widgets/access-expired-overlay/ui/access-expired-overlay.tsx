"use client";

import { type ExpiredAccessDto, accessStatusQueryOptions } from "@/entities/access-plan";
import { currentOnboardingQueryOptions } from "@/entities/plan-onboarding";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import { routes } from "@/shared/config/routes";
import { formatNumericDate } from "@/shared/lib/date";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { useQuery } from "@tanstack/react-query";
import { useSession } from "next-auth/react";
import Link from "next/link";
import { useState } from "react";
import { readExpiredDismissed, rememberExpiredDismissed } from "../model/dismiss";

/**
 * Глобальная модалка «срок доступа истёк» (#687). Mount'ится в `(app)/layout.tsx`
 * рядом с {@link OnboardingOverlay} и сидит идлом, пока `/access/me/access-status/`
 * не сообщит о недавно истёкшем доступе, который сейчас ничем не покрыт.
 *
 * Внешний компонент только тащит query — внутренний `<AccessExpiredDialog>`
 * появляется лишь когда есть что показать.
 */
export function AccessExpiredOverlay() {
  const isAuthenticated = useIsAuthenticated();
  const { data: session, status } = useSession();
  const { data } = useQuery({
    ...accessStatusQueryOptions(),
    enabled: isAuthenticated,
  });
  // Тот же query, что тащит OnboardingOverlay — react-query дедупит по ключу, лишнего
  // запроса нет. Нужен чтобы не стэкать нашу модалку поверх онбординг-визарда.
  const { data: onboarding } = useQuery({
    ...currentOnboardingQueryOptions,
    enabled: isAuthenticated,
  });

  // Уступаем профильному гейту «Как вас называть?» и плановому онбордингу — не стэкаем модалки.
  const displayNameGateActive = status === "authenticated" && session?.user.displayName === "";
  const onboardingActive = Boolean(onboarding);

  const expired = data?.recentlyExpired;
  if (!expired || displayNameGateActive || onboardingActive) return null;

  // key по grantId — новый истёкший доступ перемонтирует диалог и заново читает «свёрнуто».
  return <AccessExpiredDialog key={expired.grantId} expired={expired} />;
}

function AccessExpiredDialog({ expired }: { expired: ExpiredAccessDto }) {
  const [dismissed, setDismissed] = useState(() => readExpiredDismissed(expired.grantId));

  const dismiss = () => {
    rememberExpiredDismissed(expired.grantId);
    setDismissed(true);
  };

  return (
    <Dialog
      open={!dismissed}
      onOpenChange={(open) => {
        if (!open) dismiss();
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <span className="mb-1 flex size-11 items-center justify-center rounded-xl bg-orange-dim text-orange">
            <Icons.clock className="size-5" />
          </span>
          <DialogTitle>Срок доступа истёк</DialogTitle>
          <DialogDescription>
            Доступ по плану «{expired.planName}» закончился {formatNumericDate(expired.expiredAt)}.
            Продлите доступ — при доплате до полного доступа уже оплаченное идёт в зачёт.
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="ghost" onClick={dismiss}>
            Позже
          </Button>
          <Button asChild onClick={dismiss}>
            <Link href={routes.pricing}>
              <Icons.unlocked className="size-4" />
              Продлить доступ
            </Link>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
