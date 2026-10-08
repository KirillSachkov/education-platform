"use client";

import { useLinkStatus } from "next/link";
import { cn } from "@/shared/lib/css";

/**
 * Pending-indicator для навигационных `<Link>`. Должен рендериться ВНУТРИ
 * `<Link>` (читает `pending` из контекста `next/link`). Появляется через
 * 100ms после клика — fast navigation (<100ms, обычно prefetch'нутая страница)
 * не мигает индикатором; slow navigation видна как пульсирующая точка.
 *
 * Используется в sidebar'ах и tab-навигации после удаления `prefetch={false}` —
 * визуальный feedback что клик сработал, страница грузится (#321).
 */
export function NavLinkPending({ className }: { className?: string }) {
  const { pending } = useLinkStatus();
  return (
    <span
      aria-hidden
      data-pending={pending || undefined}
      className={cn(
        "ml-auto inline-block size-1.5 shrink-0 rounded-full bg-current",
        "opacity-0 transition-opacity duration-150 delay-100",
        "data-[pending]:opacity-70 data-[pending]:animate-pulse",
        className,
      )}
    />
  );
}
