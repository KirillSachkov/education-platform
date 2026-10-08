"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { CourseAccessLevel } from "@/entities/course";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";

interface CourseAccessNoticeProps {
  accessLevel: CourseAccessLevel;
  className?: string;
}

interface NoticeCta {
  label: string;
  hrefKind: "login" | "pricing";
}

interface NoticeCopy {
  title: string;
  text: string;
  primary: NoticeCta;
  /** Second optional CTA — currently used only for `anonymous` to offer both
   *  «Войти» and «Посмотреть планы» on the same screen. */
  secondary?: NoticeCta;
}

const noticeCopy: Partial<Record<CourseAccessLevel, NoticeCopy>> = {
  anonymous: {
    title: "Доступ не активирован",
    text: "Войдите, чтобы продолжить, или выберите план — откроет материалы по вашему уровню.",
    primary: { label: "Войти", hrefKind: "login" },
    secondary: { label: "Посмотреть планы", hrefKind: "pricing" },
  },
  authenticated: {
    title: "Доступ не активирован",
    text: "Выберите план, чтобы открыть платные материалы курса.",
    primary: { label: "Выбрать план", hrefKind: "pricing" },
  },
};

function resolveHref(hrefKind: "login" | "pricing", returnTo: string | null): string {
  if (hrefKind === "pricing") return routes.pricing;
  if (!returnTo) return "/login";
  const qs = new URLSearchParams({ callbackUrl: returnTo }).toString();
  return `/login?${qs}`;
}

export function CourseAccessNotice({ accessLevel, className }: CourseAccessNoticeProps) {
  const copy = noticeCopy[accessLevel];
  const returnTo = usePathname();
  if (!copy) return null;
  const primaryHref = resolveHref(copy.primary.hrefKind, returnTo);
  const secondaryHref = copy.secondary ? resolveHref(copy.secondary.hrefKind, returnTo) : null;

  return (
    <div
      className={cn(
        "flex flex-col gap-3 rounded-lg border border-primary/25 bg-primary/5 px-4 py-3 sm:flex-row sm:items-center sm:justify-between",
        className,
      )}
    >
      <div className="flex min-w-0 items-start gap-3">
        <div className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-md bg-primary/12 text-primary">
          <Icons.locked className="size-4" />
        </div>
        <div className="min-w-0">
          <div className="text-sm font-semibold text-foreground">{copy.title}</div>
          <p className="mt-0.5 text-xs leading-snug text-muted-foreground sm:text-sm">
            {copy.text}
          </p>
        </div>
      </div>
      <div className="flex shrink-0 flex-col-reverse gap-2 sm:flex-row sm:items-center">
        {secondaryHref && copy.secondary && (
          <Button asChild size="sm" variant="outline">
            <Link href={secondaryHref}>{copy.secondary.label}</Link>
          </Button>
        )}
        <Button asChild size="sm">
          <Link href={primaryHref}>{copy.primary.label}</Link>
        </Button>
      </div>
    </div>
  );
}
