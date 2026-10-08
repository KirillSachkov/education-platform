"use client";

import type { AdminActiveGrantSummary } from "@/entities/admin-cross-service";
import { Badge } from "@/shared/ui/kit/badge";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/shared/ui/kit/tooltip";

const TIER_LABELS: Record<string, string> = {
  FULL_ALL: "Полный доступ",
  LEARN_ALL: "Все курсы",
  COURSE: "Курс",
  SUBSCRIPTION: "Подписка",
  FREE: "Бесплатный",
};

const SOURCE_LABELS: Record<string, string> = {
  AUTO_FREE: "Авто",
  TRIAL: "Триал",
  INVITE_LINK: "Инвайт",
  ADMIN_GRANT: "Админ",
  MIGRATION: "Миграция",
  PURCHASE: "Покупка",
  GITHUB_ORG: "GitHub",
  TELEGRAM_F1: "Telegram",
};

const dateFormatter = new Intl.DateTimeFormat("ru", { day: "2-digit", month: "2-digit", year: "numeric" });

type Props = {
  grants: AdminActiveGrantSummary[] | undefined;
  isLoading: boolean;
};

const MAX_VISIBLE = 2;

export function UserPlansCell({ grants, isLoading }: Props) {
  if (isLoading) {
    return <span className="inline-block h-5 w-24 animate-pulse rounded bg-muted/40" aria-hidden />;
  }

  if (!grants || grants.length === 0) {
    return <span className="text-xs text-muted-foreground">—</span>;
  }

  const visible = grants.slice(0, MAX_VISIBLE);
  const hidden = grants.slice(MAX_VISIBLE);

  return (
    <TooltipProvider>
      <div className="flex flex-wrap items-center gap-1">
        {visible.map((grant) => (
          <Tooltip key={grant.id}>
            <TooltipTrigger asChild>
              <Badge
                variant="secondary"
                className="max-w-[180px] truncate text-[11px] font-normal"
              >
                {grant.planDisplayName ?? TIER_LABELS[grant.planTier ?? ""] ?? "План"}
              </Badge>
            </TooltipTrigger>
            <TooltipContent>
              <div className="space-y-1 text-xs">
                <div className="font-medium">{grant.planDisplayName ?? grant.planId}</div>
                {grant.planTier ? (
                  <div className="text-muted-foreground">
                    {TIER_LABELS[grant.planTier] ?? grant.planTier}
                  </div>
                ) : null}
                <div className="text-muted-foreground">
                  Источник: {SOURCE_LABELS[grant.source] ?? grant.source}
                </div>
                {grant.expiresAt ? (
                  <div className="text-muted-foreground">
                    Истекает: {dateFormatter.format(new Date(grant.expiresAt))}
                  </div>
                ) : (
                  <div className="text-muted-foreground">Бессрочно</div>
                )}
              </div>
            </TooltipContent>
          </Tooltip>
        ))}
        {hidden.length > 0 ? (
          <Tooltip>
            <TooltipTrigger asChild>
              <Badge variant="outline" className="text-[11px] font-normal">
                +{hidden.length}
              </Badge>
            </TooltipTrigger>
            <TooltipContent>
              <ul className="space-y-1 text-xs">
                {hidden.map((g) => (
                  <li key={g.id}>
                    {g.planDisplayName ?? TIER_LABELS[g.planTier ?? ""] ?? g.planId}
                  </li>
                ))}
              </ul>
            </TooltipContent>
          </Tooltip>
        ) : null}
      </div>
    </TooltipProvider>
  );
}
