import Link from "next/link";
import type { ReactNode } from "react";
import { type LockReason, resolveLockCopy } from "@/shared/lib/lock-copy";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";

interface LockCalloutProps {
  reason: LockReason | null | undefined;
  /** Optional course title used to personalise the CTA/subtitle. */
  courseTitle?: string | null;
  /** Absolute href for the CTA button. Omit to render without CTA. */
  ctaHref?: string | null;
  /**
   * Override for the CTA label. Defaults to the copy for the given reason.
   * Useful when the surrounding list controls navigation itself (search-item).
   */
  ctaLabel?: string;
  /**
   * Optional secondary CTA — обычно используется только для `anonymous`,
   * чтобы дать два пути: "Войти" + "Посмотреть планы". Если href пустой,
   * вторая кнопка не рендерится. Label берётся из `resolveLockCopy().secondaryCta`
   * если не передан явно.
   */
  secondaryCtaHref?: string | null;
  secondaryCtaLabel?: string;
  className?: string;
}

function CtaLink({
  href,
  children,
  className,
}: {
  href: string;
  children: ReactNode;
  className?: string;
}) {
  // Внешний URL — обычный `<a target="_blank">`. Внутренний — Next.js `<Link>`,
  // чтобы остаться в client-side router и не делать полный page reload.
  if (/^https?:\/\//.test(href)) {
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" className={className}>
        {children}
      </a>
    );
  }
  return (
    <Link href={href} className={className}>
      {children}
    </Link>
  );
}

/**
 * Lock callout shown in Popover above locked cards. Content-only — assumes
 * the container (typically PopoverContent) provides the outer border/padding.
 */
export function LockCallout({
  reason,
  courseTitle,
  ctaHref,
  ctaLabel,
  secondaryCtaHref,
  secondaryCtaLabel,
  className,
}: LockCalloutProps) {
  const copy = resolveLockCopy(reason, courseTitle);
  const resolvedSecondaryLabel = secondaryCtaLabel ?? copy.secondaryCta;
  const showSecondary = Boolean(secondaryCtaHref && resolvedSecondaryLabel);
  return (
    <div className={cn("flex flex-col gap-3.5", className)}>
      <div className="flex items-start gap-3">
        <span className="relative inline-flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/12 ring-1 ring-inset ring-primary/25">
          <Icons.locked className="size-4 text-primary" />
          <span
            aria-hidden
            className="absolute inset-0 rounded-xl bg-primary/10 blur-md opacity-70"
          />
        </span>
        <div className="min-w-0 flex-1 pt-0.5">
          <p className="text-sm font-semibold text-foreground leading-snug">{copy.title}</p>
          <p className="mt-1 text-[12.5px] text-muted-foreground leading-relaxed">
            {copy.subtitle}
          </p>
        </div>
      </div>
      {(ctaHref || showSecondary) && (
        <div className="flex flex-col gap-2">
          {ctaHref && (
            <Button
              asChild
              size="sm"
              className="h-9 w-full font-semibold shadow-sm shadow-primary/15"
            >
              <CtaLink href={ctaHref}>
                <span className="truncate">{ctaLabel ?? copy.cta}</span>
              </CtaLink>
            </Button>
          )}
          {showSecondary && secondaryCtaHref && (
            <Button asChild size="sm" variant="outline" className="h-9 w-full font-semibold">
              <CtaLink href={secondaryCtaHref}>
                <span className="truncate">{resolvedSecondaryLabel}</span>
              </CtaLink>
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
