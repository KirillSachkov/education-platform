"use client";

import type {
  AdminPostPurchaseGrant,
  AdminTelegramMembershipStatus,
} from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { formatDate } from "../lib/format";
import { useRecheckTelegramMembership } from "../model/use-recheck-telegram-membership";
import { useResendPlanWelcome } from "../model/use-resend-plan-welcome";
import { OnboardingChecklist } from "./onboarding-checklist";
import { PlanTelegramChats } from "./plan-telegram-chats";

type GrantBlockProps = {
  userId: string;
  grant: AdminPostPurchaseGrant;
};

/**
 * Один активный грант (#444): план/tier/source/дата, onboarding-прогресс,
 * Telegram-членство, чаты плана и per-plan support-действия (перепроверить
 * членство, переотправить приветствие).
 */
export function GrantBlock({ userId, grant }: GrantBlockProps) {
  const recheck = useRecheckTelegramMembership(userId);
  const resendWelcome = useResendPlanWelcome(userId);

  const planName = grant.planDisplayName ?? `план ${grant.planId.slice(0, 8)}`;

  return (
    <Card>
      <CardContent className="space-y-4 p-4">
        {/* Header: plan name + tier/source */}
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div className="min-w-0 space-y-1">
            <p className="text-sm font-medium break-words">{planName}</p>
            <div className="flex flex-wrap gap-1.5">
              <Badge variant="outline" className="text-[11px]">
                {grant.planTier ?? "—"}
              </Badge>
              <Badge variant="outline" className="text-[11px]">
                {grant.source}
              </Badge>
            </div>
          </div>
          <span className="shrink-0 text-xs text-muted-foreground">
            выдан {formatDate(grant.grantedAt)}
          </span>
        </div>

        {/* Onboarding */}
        <div className="border-t border-border/40 pt-3">
          <OnboardingChecklist onboarding={grant.onboarding} />
        </div>

        {/* Telegram membership */}
        <div className="flex flex-wrap items-center gap-2 border-t border-border/40 pt-3 text-xs">
          <span className="font-medium text-foreground">Telegram-членство:</span>
          <MembershipBadge telegram={grant.telegram} />
        </div>

        {/* Per-plan telegram chats */}
        <div className="space-y-2 border-t border-border/40 pt-3">
          <p className="text-xs font-medium text-foreground">Чаты плана</p>
          <PlanTelegramChats planId={grant.planId} />
        </div>

        {/* Per-plan support actions */}
        <div className="flex flex-wrap gap-2 border-t border-border/40 pt-3">
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="min-touch gap-1.5"
            disabled={recheck.isPending}
            onClick={() => recheck.mutate({ planId: grant.planId })}
          >
            {recheck.isPending ? (
              <Icons.loading className="size-3.5 animate-spin" />
            ) : (
              <Icons.refresh className="size-3.5" />
            )}
            Перепроверить членство
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="min-touch gap-1.5"
            disabled={resendWelcome.isPending}
            onClick={() => resendWelcome.mutate({ planId: grant.planId })}
          >
            {resendWelcome.isPending ? (
              <Icons.loading className="size-3.5 animate-spin" />
            ) : (
              <Icons.send className="size-3.5" />
            )}
            Переотправить приветствие
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

const MEMBERSHIP_LABEL: Record<AdminTelegramMembershipStatus, string> = {
  member: "в чате",
  not_member: "не в чате",
  unknown: "неизвестно",
  "n/a": "не применимо",
};

function MembershipBadge({
  telegram,
}: {
  telegram: AdminPostPurchaseGrant["telegram"];
}) {
  const variant: "secondary" | "outline" | "destructive" =
    telegram.status === "member"
      ? "secondary"
      : telegram.status === "not_member"
        ? "destructive"
        : "outline";
  return (
    <Badge variant={variant} className="text-[11px]">
      {MEMBERSHIP_LABEL[telegram.status]}
    </Badge>
  );
}
