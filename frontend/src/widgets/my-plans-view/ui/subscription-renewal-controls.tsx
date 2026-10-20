import type { PlanGrantDto } from "@/entities/access-plan";
import { formatNumericDate } from "@/shared/lib/date";

export function hasSubscriptionLifecycle(grant: PlanGrantDto): boolean {
  return Boolean(grant.nextChargeAt || grant.renewalGraceEndsAt || grant.autoRenewalCancelledAt);
}

export function SubscriptionRenewalControls({ grant }: { grant: PlanGrantDto }) {
  const accessEndsAt = grant.accessEndsAt ?? grant.expiresAt;
  return (
    <section className="mt-4 rounded-xl border border-border/50 bg-muted/20 p-3 sm:p-4">
      <h3 className="text-sm font-medium">Автопродление отключено</h3>
      <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
        Новых списаний не будет. Новые покупки и продления закрыты.
      </p>
      {accessEndsAt ? (
        <p className="mt-2 text-sm">Доступ до {formatNumericDate(accessEndsAt)}</p>
      ) : null}
    </section>
  );
}
