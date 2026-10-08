"use client";

import type { UpgradeQuoteDto } from "@/entities/access-plan";

/**
 * Phase 2 #112 — breakdown скидки (credit) перед кнопкой оплаты.
 * Показывает: «–25 000 ₽ за курс React» + итоговую цену со скидкой.
 * Общий для каталога планов и detail-страницы плана (#486).
 */
export function UpgradeCreditBreakdown({
  quote,
  originalPrice,
}: {
  quote: UpgradeQuoteDto;
  originalPrice: string;
}) {
  if (quote.finalPriceCents == null || quote.creditCents <= 0) return null;

  const finalRubles = Math.floor(quote.finalPriceCents / 100);
  const creditRubles = Math.floor(quote.creditCents / 100);

  return (
    <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/5 px-4 py-3 text-sm">
      <div className="flex items-baseline justify-between gap-3">
        <span className="font-medium text-emerald-700 dark:text-emerald-400">К оплате</span>
        <span className="font-bold text-2xl tracking-tight text-emerald-700 dark:text-emerald-400">
          {finalRubles.toLocaleString("ru-RU")} ₽
        </span>
      </div>
      <div className="mt-1 flex items-baseline justify-between gap-3 text-xs text-muted-foreground">
        <span className="line-through">было {originalPrice}</span>
        <span>скидка {creditRubles.toLocaleString("ru-RU")} ₽</span>
      </div>
      {quote.sources.length > 0 && (
        <ul className="mt-2 space-y-0.5 border-t border-emerald-500/20 pt-2 text-[11px] text-muted-foreground">
          {quote.sources.map((s) => (
            <li key={s.grantId} className="flex items-center justify-between gap-2">
              <span className="truncate">за «{s.planDisplayName}»</span>
              <span className="font-medium">
                −{Math.floor(s.creditCents / 100).toLocaleString("ru-RU")} ₽
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
