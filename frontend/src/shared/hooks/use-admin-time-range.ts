"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";

import {
  DEFAULT_RANGE_PRESETS,
  type DateRangePreset,
  type DateRangeValue,
} from "@/shared/ui/kit/date-range-picker";

const DEFAULT_PRESET_ID = "30d";
const RANGE_PARAM = "range";
const FROM_PARAM = "from";
const TO_PARAM = "to";

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

function isValidIsoDate(value: string | null | undefined): value is string {
  return typeof value === "string" && ISO_DATE.test(value);
}

function presetById(id: string | null): DateRangePreset | undefined {
  if (!id) return undefined;
  return DEFAULT_RANGE_PRESETS.find((p) => p.id === id);
}

function presetToRange(preset: DateRangePreset): DateRangeValue {
  const today = new Date();
  const from = new Date();
  from.setDate(today.getDate() - (preset.days - 1));
  const iso = (d: Date) => d.toISOString().slice(0, 10);
  return { from: iso(from), to: iso(today) };
}

type UseAdminTimeRangeResult = {
  /** Resolved date range — defaults to the 30-day preset when nothing is in the URL. */
  range: DateRangeValue;
  /** Active preset id (`7d|30d|90d|all`) or `undefined` for a custom range. */
  presetId: string | undefined;
  /** Pick a preset — writes `?range=` and clears `?from=&to=`. */
  setPreset: (preset: DateRangePreset) => void;
  /** Set a custom range — writes `?from=&to=` and clears `?range=`. Empty `from`/`to` removes both params (defaults). */
  setCustom: (range: DateRangeValue) => void;
};

/**
 * URL-state driven time range for the admin dashboard.
 *
 * - `?range=7d|30d|90d|all` — preset shorthand (single source of truth when present).
 * - `?from=YYYY-MM-DD&to=YYYY-MM-DD` — explicit custom range (overrides preset if both present).
 * - Default (no params) — 30-day preset.
 */
export function useAdminTimeRange(): UseAdminTimeRangeResult {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const fromParam = searchParams.get(FROM_PARAM);
  const toParam = searchParams.get(TO_PARAM);
  const rangeParam = searchParams.get(RANGE_PARAM);

  let range: DateRangeValue;
  let presetId: string | undefined;

  if (isValidIsoDate(fromParam) && isValidIsoDate(toParam)) {
    range = { from: fromParam, to: toParam };
    presetId = undefined;
  } else {
    const preset = presetById(rangeParam) ?? presetById(DEFAULT_PRESET_ID);
    range = preset ? presetToRange(preset) : {};
    presetId = preset?.id;
  }

  const write = (nextRangeId: string | null, nextFrom: string | null, nextTo: string | null) => {
    const params = new URLSearchParams(searchParams.toString());
    if (nextRangeId) {
      params.set(RANGE_PARAM, nextRangeId);
    } else {
      params.delete(RANGE_PARAM);
    }
    if (nextFrom) {
      params.set(FROM_PARAM, nextFrom);
    } else {
      params.delete(FROM_PARAM);
    }
    if (nextTo) {
      params.set(TO_PARAM, nextTo);
    } else {
      params.delete(TO_PARAM);
    }
    const search = params.toString();
    router.replace(search ? `${pathname}?${search}` : pathname, { scroll: false });
  };

  const setPreset = (preset: DateRangePreset) => {
    write(preset.id, null, null);
  };

  const setCustom = (next: DateRangeValue) => {
    if (!next.from && !next.to) {
      write(null, null, null);
      return;
    }
    write(null, next.from ?? null, next.to ?? null);
  };

  return { range, presetId, setPreset, setCustom };
}
