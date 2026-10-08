"use client";

import { format, parse } from "date-fns";
import { ru } from "date-fns/locale";
import { CalendarIcon, X } from "lucide-react";
import { type ComponentProps, useState } from "react";
import type { DateRange as DayPickerRange } from "react-day-picker";

import { Button } from "@/shared/ui/kit/button";
import { Calendar } from "@/shared/ui/kit/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { cn } from "@/shared/lib/css";

const ISO_FORMAT = "yyyy-MM-dd";
const DISPLAY_FORMAT = "dd.MM.yyyy";

function parseIso(value: string | undefined): Date | undefined {
  if (!value) return undefined;
  const date = parse(value, ISO_FORMAT, new Date());
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function formatIso(date: Date | undefined): string | undefined {
  return date ? format(date, ISO_FORMAT) : undefined;
}

export type DateRangeValue = {
  from?: string;
  to?: string;
};

export type DateRangePreset = {
  id: string;
  label: string;
  /** Number of days to subtract from today for `from`. `to` is always today. `1` means "today only". */
  days: number;
};

export const DEFAULT_RANGE_PRESETS: DateRangePreset[] = [
  { id: "7d", label: "7 дней", days: 7 },
  { id: "30d", label: "30 дней", days: 30 },
  { id: "90d", label: "90 дней", days: 90 },
  { id: "all", label: "Всё время", days: 365 },
];

type DateRangePickerProps = {
  value: DateRangeValue;
  /** Called when the user picks a custom range via the calendar. */
  onChange: (value: DateRangeValue) => void;
  /** Optional callback for preset-chip clicks. Falls back to `onChange` with the resolved range. */
  onPresetSelect?: (preset: DateRangePreset) => void;
  presets?: DateRangePreset[];
  className?: string;
  align?: ComponentProps<typeof PopoverContent>["align"];
  buttonClassName?: string;
  disabledDates?: ComponentProps<typeof Calendar>["disabled"];
  /** Currently-active preset id. Highlights the matching chip + serves as the button label. */
  presetId?: string;
};

export function presetToRange(preset: DateRangePreset): DateRangeValue {
  const today = new Date();
  const from = new Date();
  from.setDate(today.getDate() - (preset.days - 1));
  return { from: formatIso(from), to: formatIso(today) };
}

export function DateRangePicker({
  value,
  onChange,
  onPresetSelect,
  presets = DEFAULT_RANGE_PRESETS,
  className,
  align = "end",
  buttonClassName,
  disabledDates,
  presetId,
}: DateRangePickerProps) {
  const [open, setOpen] = useState(false);

  const range: DayPickerRange = {
    from: parseIso(value.from),
    to: parseIso(value.to),
  };

  const fromDate = range.from;
  const toDate = range.to;

  const matchedPreset = presets.find((p) => p.id === presetId);

  let label: string;
  if (matchedPreset) {
    label = matchedPreset.label;
  } else if (fromDate && toDate) {
    label = `${format(fromDate, DISPLAY_FORMAT, { locale: ru })} – ${format(toDate, DISPLAY_FORMAT, { locale: ru })}`;
  } else {
    label = "Период";
  }

  const applyPreset = (preset: DateRangePreset) => {
    if (onPresetSelect) {
      onPresetSelect(preset);
    } else {
      onChange(presetToRange(preset));
    }
    setOpen(false);
  };

  const handleRangeChange = (next: DayPickerRange | undefined) => {
    onChange({ from: formatIso(next?.from), to: formatIso(next?.to) });
  };

  const clear = () => onChange({});

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          className={cn("h-9 gap-2 px-3 font-normal", buttonClassName, className)}
        >
          <CalendarIcon className="size-4 shrink-0 opacity-70" />
          <span className="text-sm">{label}</span>
          {fromDate || toDate ? (
            <span
              role="button"
              tabIndex={0}
              aria-label="Очистить период"
              className="ml-1 inline-flex size-4 items-center justify-center rounded-sm opacity-70 transition-colors hover:bg-accent hover:opacity-100"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                clear();
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  event.stopPropagation();
                  clear();
                }
              }}
            >
              <X className="size-3" />
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="flex w-auto flex-col gap-3 p-3" align={align}>
        <div className="flex flex-wrap gap-1.5">
          {presets.map((preset) => (
            <Button
              key={preset.id}
              type="button"
              size="sm"
              variant={presetId === preset.id ? "default" : "outline"}
              onClick={() => applyPreset(preset)}
              className="h-7 px-2.5 text-xs"
            >
              {preset.label}
            </Button>
          ))}
        </div>
        <Calendar
          mode="range"
          selected={range}
          onSelect={handleRangeChange}
          numberOfMonths={2}
          defaultMonth={fromDate ?? undefined}
          disabled={disabledDates}
        />
      </PopoverContent>
    </Popover>
  );
}
