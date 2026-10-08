"use client";

import { format, parse } from "date-fns";
import { ru } from "date-fns/locale";
import { CalendarIcon, X } from "lucide-react";
import { type ComponentProps } from "react";

import { Button } from "@/shared/ui/kit/button";
import { Calendar } from "@/shared/ui/kit/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/ui/kit/popover";
import { cn } from "@/shared/lib/css";

const ISO_FORMAT = "yyyy-MM-dd";
const DISPLAY_FORMAT = "dd.MM.yyyy";
const PLACEHOLDER = "дд.мм.гггг";

function parseIso(value: string | undefined): Date | undefined {
  if (!value) return undefined;
  const date = parse(value, ISO_FORMAT, new Date());
  return Number.isNaN(date.getTime()) ? undefined : date;
}

function formatIso(date: Date | undefined): string | undefined {
  return date ? format(date, ISO_FORMAT) : undefined;
}

type DatePickerProps = {
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  buttonClassName?: string;
  align?: ComponentProps<typeof PopoverContent>["align"];
  allowClear?: boolean;
  disabledDates?: ComponentProps<typeof Calendar>["disabled"];
};

export function DatePicker({
  value,
  onChange,
  placeholder = PLACEHOLDER,
  disabled,
  className,
  buttonClassName,
  align = "start",
  allowClear = true,
  disabledDates,
}: DatePickerProps) {
  const date = parseIso(value);

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          className={cn(
            "h-9 w-[150px] justify-start gap-2 px-3 font-normal",
            !date && "text-muted-foreground",
            buttonClassName,
            className,
          )}
        >
          <CalendarIcon className="size-4 shrink-0 opacity-70" />
          <span className="flex-1 truncate text-left text-sm">
            {date ? format(date, DISPLAY_FORMAT, { locale: ru }) : placeholder}
          </span>
          {allowClear && date ? (
            <span
              role="button"
              tabIndex={0}
              aria-label="Очистить дату"
              className="ml-auto inline-flex size-4 items-center justify-center rounded-sm opacity-70 transition-colors hover:bg-accent hover:opacity-100"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onChange(undefined);
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  event.stopPropagation();
                  onChange(undefined);
                }
              }}
            >
              <X className="size-3" />
            </span>
          ) : null}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-auto p-0" align={align}>
        <Calendar
          mode="single"
          selected={date}
          onSelect={(next) => onChange(formatIso(next))}
          disabled={disabledDates}
          autoFocus
        />
      </PopoverContent>
    </Popover>
  );
}
