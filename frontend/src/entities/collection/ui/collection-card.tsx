"use client";

import { resolveLockCopy } from "@/shared/lib/lock-copy";
import { cn } from "@/shared/lib/css";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import { Icons } from "@/shared/ui/icons";
import { AccessTypeBadge } from "@/shared/ui/components/access-type-badge";
import Link from "next/link";
import type { CollectionSummaryDto } from "../types";
import { CollectionCoverImage } from "./collection-cover-image";

interface CollectionCardProps {
  collection: CollectionSummaryDto;
  href: string;
  /**
   * `row` — узкая портретная плитка (для scroll-полос), aspect 3:4.
   * `tile` — крупная карточка в grid, aspect 3:4 портрет.
   * `admin-row` — горизонтальная строка для course-builder/space-nav списков:
   *   обложка-портрет + заголовок + описание + бейджи доступа/статуса.
   */
  variant?: "row" | "tile" | "admin-row";
  /**
   * Дополнительный slot для админских действий (edit, delete) — справа от бейджей.
   * Видно только в variant="admin-row".
   */
  adminActions?: React.ReactNode;
  /**
   * Бейдж статуса публикации. Показывается только в variant="admin-row".
   * Переиспользуем тот же shape, что используется в админ-таблицах.
   */
  statusBadge?: { label: string; className: string } | null;
}

const GRADIENT_CLASSES = [
  "from-slate-700 via-slate-800 to-slate-900",
  "from-indigo-700 via-indigo-900 to-slate-900",
  "from-teal-700 via-teal-900 to-slate-900",
  "from-rose-700 via-rose-900 to-zinc-900",
  "from-amber-700 via-amber-900 to-stone-900",
  "from-violet-700 via-violet-900 to-slate-900",
];

/**
 * Стабильный градиент-по-id (детерминированный хэш) — используется как fallback
 * фон для подборок без обложки в карточках и в collection-detail-view.
 */
export function getCollectionGradient(id: string): string {
  let hash = 0;
  for (let i = 0; i < id.length; i++) {
    hash = (hash << 5) - hash + id.charCodeAt(i);
    hash |= 0;
  }
  return GRADIENT_CLASSES[Math.abs(hash) % GRADIENT_CLASSES.length];
}

export function CollectionCard({
  collection,
  href,
  variant = "row",
  adminActions,
  statusBadge,
}: CollectionCardProps) {
  if (variant === "admin-row") {
    return (
      <AdminRow
        collection={collection}
        href={href}
        adminActions={adminActions}
        statusBadge={statusBadge}
      />
    );
  }

  const isTile = variant === "tile";
  const showLock = !collection.isAccessible && collection.lockReason !== null;
  const lockHint =
    showLock && collection.lockReason
      ? resolveLockCopy(collection.lockReason, collection.courseTitle).shortHint
      : null;

  // Подборка всегда открывается на свой detail (`/collections/[id]`) — он
  // partial-access: структура + per-item замки, тело материалов не утекает.
  // Раньше locked-подборка вела на /login (форс-редирект) — убрано (#385).

  return (
    <Link
      href={href}
      className={cn(
        "group relative flex overflow-hidden rounded-xl border border-border/40 transition-all hover:border-border hover:shadow-md",
        isTile
          ? "aspect-[3/4] w-full flex-col justify-end min-h-[220px]"
          : "h-60 w-44 shrink-0 flex-col justify-end",
      )}
      title={lockHint ?? undefined}
    >
      <CollectionCoverImage
        src={collection.coverImageUrl}
        alt=""
        fill
        sizes={isTile ? "(max-width: 640px) 50vw, (max-width: 1024px) 33vw, 25vw" : "176px"}
        className="object-cover transition-transform group-hover:scale-105"
        fallback={
          <div
            className={cn(
              "absolute inset-0 flex items-center justify-center bg-gradient-to-br",
              getCollectionGradient(collection.id),
            )}
          >
            <Icons.layers className="size-10 text-white/35" aria-hidden />
          </div>
        }
      />

      <div
        className={cn(
          "absolute inset-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent",
          showLock && "from-black/90 via-black/50 backdrop-blur-[1px]",
        )}
      />

      {showLock && (
        <div
          className="absolute right-2 top-2 z-20 flex size-8 items-center justify-center rounded-full bg-black/55 text-white/95 backdrop-blur-sm"
          title={lockHint ?? undefined}
          aria-label={lockHint ?? undefined}
        >
          <Icons.locked className="size-4" />
        </div>
      )}

      <div className={cn("relative z-10", isTile ? "p-3 sm:p-4" : "p-3 pt-0")}>
        <p
          className={cn(
            "line-clamp-2 font-semibold leading-tight text-white",
            isTile ? "text-sm sm:text-base" : "text-sm",
          )}
        >
          {collection.title}
        </p>
        <p className={cn("mt-1 text-white/70", isTile ? "text-[11px] sm:text-xs" : "text-[11px]")}>
          {formatRuPlural(collection.itemCount, RU_PLURALS.material)}
        </p>
      </div>
    </Link>
  );
}

function AdminRow({
  collection,
  href,
  adminActions,
  statusBadge,
}: {
  collection: CollectionSummaryDto;
  href: string;
  adminActions?: React.ReactNode;
  statusBadge?: { label: string; className: string } | null;
}) {
  return (
    <Link
      href={href}
      prefetch={false}
      className="group flex items-center gap-4 rounded-xl border border-border/60 bg-card p-3 transition-colors hover:bg-accent/20 hover:border-primary/40"
    >
      <div className="relative h-16 w-12 shrink-0 overflow-hidden rounded-lg">
        <CollectionCoverImage
          src={collection.coverImageUrl}
          alt=""
          fill
          sizes="48px"
          className="object-cover"
          fallback={
            <div
              className={cn(
                "absolute inset-0 flex items-center justify-center bg-gradient-to-br",
                getCollectionGradient(collection.id),
              )}
            >
              <Icons.layers className="size-5 text-white/35" aria-hidden />
            </div>
          }
        />
        <div className="absolute inset-x-0 bottom-0 flex justify-center bg-black/50 py-0.5 text-[10px] font-medium text-white/90 backdrop-blur-sm">
          <span className="inline-flex items-center gap-0.5">
            <Icons.grid className="size-2.5" />
            {collection.itemCount}
          </span>
        </div>
      </div>

      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium line-clamp-1 group-hover:text-primary transition-colors">
          {collection.title}
        </p>
        {collection.description && (
          <p className="mt-0.5 line-clamp-1 text-xs text-muted-foreground">
            {collection.description}
          </p>
        )}
      </div>

      <div className="flex shrink-0 items-center gap-1.5">
        <AccessTypeBadge accessType={collection.accessType} />
        {statusBadge && (
          <span
            className={cn(
              "rounded-md border px-1.5 py-0.5 text-[10px] font-medium",
              statusBadge.className,
            )}
          >
            {statusBadge.label}
          </span>
        )}
        {adminActions}
      </div>
    </Link>
  );
}
