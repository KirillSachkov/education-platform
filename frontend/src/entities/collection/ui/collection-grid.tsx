"use client";

import { useState } from "react";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { formatRuPlural, RU_PLURALS } from "@/shared/lib/pluralize";
import type { CollectionSummaryDto } from "../types";
import { CollectionCard } from "./collection-card";

interface CollectionGridProps {
  collections: CollectionSummaryDto[];
  /** Function to resolve the href for each collection card */
  getHref: (collectionId: string) => string;
  /**
   * Начальное число показанных подборок (дальше — по клику «Показать ещё»).
   * По умолчанию 8.
   */
  initialCount?: number;
  /** Размер одной «порции» при клике «Показать ещё». По умолчанию 8. */
  step?: number;
  /** Заголовок секции. `null`/`undefined` — заголовок не рендерится. */
  title?: string | null;
  /** Доп. слот справа от заголовка (например, кнопка «Создать»). */
  actions?: React.ReactNode;
  /**
   * Переопределяет grid-классы. На главной нужно пустить подборки в +1 колонку
   * относительно курсовой сетки, чтобы 3:4-плитки не превышали по размеру курсовые
   * 16:9 + content карточки.
   */
  gridClassName?: string;
}

/**
 * Сетка подборок с прогрессивным раскрытием: рендерим первые `initialCount` карточек,
 * дальше — по кнопке «Показать ещё» добавляем очередные `step` штук. Заменяет прежние
 * горизонтальные карусели — UX-контракт один во всех контекстах (главная, курс, КБ).
 */
export function CollectionGrid({
  collections,
  getHref,
  initialCount = 8,
  step = 8,
  title = "Подборки",
  actions,
  gridClassName,
}: CollectionGridProps) {
  const [visibleCount, setVisibleCount] = useState(initialCount);

  if (collections.length === 0 && !actions) return null;

  const showHeader = Boolean(title) || Boolean(actions);
  const visible = collections.slice(0, visibleCount);
  const hasMore = collections.length > visibleCount;
  const remaining = collections.length - visibleCount;

  return (
    <div className="@container space-y-3">
      {showHeader && (
        <div className="flex items-center justify-between gap-3">
          {title ? <h2 className="text-lg font-bold">{title}</h2> : <span />}
          {actions}
        </div>
      )}

      <div className={gridClassName ?? "grid grid-cols-2 gap-3 @2xl:grid-cols-3 @4xl:grid-cols-4"}>
        {visible.map((collection) => (
          <CollectionCard
            key={collection.id}
            collection={collection}
            href={getHref(collection.id)}
            variant="tile"
          />
        ))}
      </div>

      {hasMore && (
        <div className="flex justify-center pt-1">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setVisibleCount((n) => n + step)}
            className="gap-1.5"
          >
            Показать ещё
            <span className="text-muted-foreground tabular-nums">
              {remaining < step ? formatRuPlural(remaining, RU_PLURALS.collection) : `+${step}`}
            </span>
            <Icons.chevronRight className="size-3.5 rotate-90" />
          </Button>
        </div>
      )}
    </div>
  );
}
