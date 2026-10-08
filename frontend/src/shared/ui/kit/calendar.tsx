"use client";

import { ru } from "date-fns/locale";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { ComponentProps } from "react";
import { DayPicker } from "react-day-picker";

import { cn } from "@/shared/lib/css";

export type CalendarProps = ComponentProps<typeof DayPicker>;

export function Calendar({ className, classNames, showOutsideDays = true, ...props }: CalendarProps) {
  return (
    <DayPicker
      locale={ru}
      weekStartsOn={1}
      showOutsideDays={showOutsideDays}
      className={cn("p-3", className)}
      classNames={{
        months: "flex flex-col sm:flex-row gap-2",
        month: "flex flex-col gap-3",
        month_caption: "flex justify-center pt-1 relative items-center",
        caption_label: "text-sm font-medium capitalize",
        nav: "absolute inset-x-0 flex items-center justify-between px-1 pt-1",
        button_previous:
          "inline-flex h-7 w-7 items-center justify-center rounded-md border border-input bg-transparent text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:opacity-30",
        button_next:
          "inline-flex h-7 w-7 items-center justify-center rounded-md border border-input bg-transparent text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:opacity-30",
        month_grid: "w-full border-collapse",
        weekdays: "flex",
        weekday: "text-muted-foreground w-9 font-normal text-[0.75rem] capitalize",
        week: "flex w-full mt-1",
        day: "size-9 text-center text-sm p-0 relative focus-within:relative focus-within:z-20",
        day_button:
          "size-9 inline-flex items-center justify-center rounded-md font-normal text-foreground transition-colors hover:bg-accent hover:text-accent-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        selected:
          "[&_button]:bg-primary [&_button]:text-primary-foreground [&_button]:hover:bg-primary [&_button]:hover:text-primary-foreground",
        today: "[&_button]:bg-accent/50 [&_button]:font-semibold",
        outside: "text-muted-foreground/40",
        disabled: "text-muted-foreground/30 [&_button]:hover:bg-transparent [&_button]:hover:text-muted-foreground/30",
        range_start:
          "[&_button]:bg-primary [&_button]:text-primary-foreground [&_button]:rounded-r-none",
        range_middle:
          "[&_button]:bg-accent [&_button]:text-foreground [&_button]:rounded-none",
        range_end:
          "[&_button]:bg-primary [&_button]:text-primary-foreground [&_button]:rounded-l-none",
        hidden: "invisible",
        ...classNames,
      }}
      components={{
        Chevron: ({ orientation }) =>
          orientation === "left" ? (
            <ChevronLeft className="size-4" />
          ) : (
            <ChevronRight className="size-4" />
          ),
      }}
      {...props}
    />
  );
}
