"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { type HomePinDto, myHomePinsQueryOptions } from "@/entities/plan-pinned-material";
import { getMaterialKindBadge } from "@/entities/material";
import { resolveUnlockHref } from "@/shared/lib/lock-copy";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { LockIconBadge } from "@/shared/ui/components/lock-icon-badge";

/**
 * Закреплённые автором материалы для home-дашборда (epic #397, S7). Тянет
 * `GET /access/me/home-pins/` — merge закрепов всех активных планов пользователя.
 * Рендерит компактную «закреп-панель» — вытянутый список строк, а не сетку
 * карточек: subtle primary-тинт + pin-eyebrow сигналят «это закреп автора».
 * Недоступные строки показывают замок + ведут в тарифы. Ничего не рендерит,
 * если список пуст (у автора нет закрепов или пользователь без grant'ов).
 *
 * Монтируется в `AuthenticatedHome` сразу ПОД `LevelStatsCard` (прогресс) и НАД
 * курсами — это «с чего начать», поэтому стоит высоко (см. frontend/CLAUDE.md
 * home composition).
 */
export function HomePinsSection() {
  const { data: pins } = useQuery(myHomePinsQueryOptions);

  if (!pins || pins.length === 0) return null;

  return (
    <section aria-labelledby="home-pins-heading">
      <div className="overflow-hidden rounded-xl border border-primary/20 bg-primary/[0.035]">
        <div className="flex items-center gap-2 border-b border-primary/15 bg-primary/[0.05] px-3.5 py-2 sm:px-4">
          <Icons.pin className="size-3.5 shrink-0 text-primary" aria-hidden />
          <h2
            id="home-pins-heading"
            className="text-[11px] sm:text-xs font-semibold uppercase tracking-[0.14em] text-primary"
          >
            Закреплено автором
          </h2>
          <span className="ml-auto text-[11px] text-muted-foreground/70 tabular-nums">
            {pins.length}
          </span>
        </div>
        <ul className="divide-y divide-border/40">
          {pins.map((pin) => (
            <li key={pin.materialId}>
              <HomePinRow pin={pin} />
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}

function HomePinRow({ pin }: { pin: HomePinDto }) {
  const kindBadge = getMaterialKindBadge(pin.kind);
  const KindIcon = kindBadge.icon;

  // Backend href = "/knowledge-base/{materialId}"; ссылку всё равно строим через routes —
  // единый источник правды для student-facing detail-роута.
  const href = routes.knowledgeBaseMaterial(pin.materialId);
  const unlockHref = pin.isAccessible
    ? null
    : (resolveUnlockHref({ lockReason: pin.lockReason, returnTo: href }) ?? routes.pricing);

  const rowHref = pin.isAccessible ? href : (unlockHref ?? href);

  return (
    <Link
      href={rowHref}
      prefetch={false}
      className="group flex min-h-[44px] items-center gap-3 px-3.5 py-2.5 transition-colors hover:bg-primary/[0.05] sm:px-4"
    >
      <span
        className={cn(
          "flex size-9 shrink-0 items-center justify-center rounded-lg",
          kindBadge.iconBgClassName,
          !pin.isAccessible && "opacity-50",
        )}
      >
        <KindIcon className="size-4" aria-hidden />
      </span>
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium text-foreground transition-colors group-hover:text-primary">
          {pin.title}
        </p>
        <p className="truncate text-xs text-muted-foreground">{pin.note ?? kindBadge.label}</p>
      </div>
      {pin.isAccessible ? (
        <Icons.arrowRight
          className="size-4 shrink-0 text-muted-foreground/40 transition-all group-hover:translate-x-0.5 group-hover:text-primary"
          aria-hidden
        />
      ) : (
        <LockIconBadge reason={pin.lockReason} variant="inline" className="shrink-0" />
      )}
    </Link>
  );
}
