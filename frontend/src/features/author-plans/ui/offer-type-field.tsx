"use client";

import type { PlanTier } from "@/entities/access-plan";
import { OFFER_TYPE_LABELS, type PlanOfferType } from "@/shared/config/offer-type";
import { Label } from "@/shared/ui/kit/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";

/**
 * Offer-type'ы, доступные для COURSE-tier плана. FULL_ACCESS отвергается бэкендом
 * на COURSE-tier; FULL_ALL → forced FULL_ACCESS (выбора нет).
 */
const COURSE_OFFER_TYPES: ReadonlyArray<PlanOfferType> = ["COURSE", "INTENSIVE", "MARATHON"];

interface OfferTypeFieldProps {
  /** Текущий tier плана. FULL_ALL → форсит FULL_ACCESS (поле readonly). */
  tier: PlanTier;
  /** Выбранный offer-type для COURSE-tier. */
  value: PlanOfferType;
  onChange: (value: PlanOfferType) => void;
}

/**
 * Селектор маркетинг-формата оффера (#418/#425). Ортогонален tier'у:
 * - FULL_ALL → формат форсится «Полный доступ» (disabled readonly-чип).
 * - COURSE → выбор из Курс / Интенсив / Марафон (default Курс).
 *
 * Используется в create/edit формах плана. Для не-COURSE/не-FULL_ALL tier'ов
 * (legacy) поле не рендерится — бэкенд сам форсит дефолт.
 */
export function OfferTypeField({ tier, value, onChange }: OfferTypeFieldProps) {
  if (tier === "FULL_ALL") {
    return (
      <section className="space-y-2">
        <Label className="text-sm font-medium">Формат оффера</Label>
        <div className="flex items-center gap-2 rounded-md border bg-muted/20 px-3 py-2 text-sm">
          <span className="rounded bg-amber-400/15 px-2 py-0.5 text-xs font-semibold text-amber-600 dark:text-amber-300">
            {OFFER_TYPE_LABELS.FULL_ACCESS}
          </span>
          <span className="text-xs text-muted-foreground">
            У плана «Полный доступ .NET Fullstack» формат зафиксирован.
          </span>
        </div>
      </section>
    );
  }

  if (tier !== "COURSE") return null;

  return (
    <section className="space-y-2">
      <Label htmlFor="plan-offer-type" className="text-sm font-medium">
        Формат оффера
      </Label>
      <Select value={value} onValueChange={(v) => onChange(v as PlanOfferType)}>
        <SelectTrigger id="plan-offer-type" className="w-full sm:w-[260px]">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          {COURSE_OFFER_TYPES.map((offer) => (
            <SelectItem key={offer} value={offer}>
              {OFFER_TYPE_LABELS[offer]}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
      <p className="text-xs text-muted-foreground">
        Влияет на то, как план показан в каталоге: «Интенсив»/«Марафон» попадают в
        отдельную секцию с цветным бейджем, «Курс» — в общий блок.
      </p>
    </section>
  );
}
