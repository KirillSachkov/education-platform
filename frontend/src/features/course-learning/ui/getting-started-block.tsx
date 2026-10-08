"use client";

import type { CurriculumSectionDto } from "@/entities/course";
import { getCourseItemHref } from "@/entities/course";
import { useCourseSlug } from "@/shared/providers/course-id-provider";
import { cn } from "@/shared/lib/css";
import { formatDurationSecondsHuman } from "@/shared/lib/duration";
import { Card } from "@/shared/ui/kit/card";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import Link from "next/link";

interface GettingStartedBlockProps {
  section: CurriculumSectionDto;
}

const ITEM_ICON: Record<string, IconComponent> = {
  Lesson: Icons.lesson,
  Material: Icons.lesson,
  Article: Icons.article,
  Issue: Icons.issue,
};

const ITEM_TYPE_LABEL: Record<string, string> = {
  Lesson: "урок",
  Material: "материал",
  Article: "статья",
  Issue: "задача",
};

const ITEM_TONE: Record<string, { bg: string; border: string; text: string }> = {
  Lesson: { bg: "bg-teal-dim", border: "border-teal/25", text: "text-teal" },
  Material: { bg: "bg-teal-dim", border: "border-teal/25", text: "text-teal" },
  Article: { bg: "bg-blue-dim", border: "border-blue/25", text: "text-blue" },
  Issue: { bg: "bg-orange-dim", border: "border-orange/25", text: "text-orange" },
};

export function GettingStartedBlock({ section }: GettingStartedBlockProps) {
  const courseSlug = useCourseSlug();

  return (
    <section>
      <div className="flex items-end justify-between gap-3 mb-3 sm:mb-4">
        <div className="flex items-center gap-2.5 sm:gap-3 min-w-0">
          <div className="size-9 sm:size-10 rounded-xl bg-primary/15 border border-primary/30 flex items-center justify-center shrink-0">
            <Icons.compass size={16} className="text-primary sm:hidden" />
            <Icons.compass size={18} className="text-primary hidden sm:block" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm sm:text-base md:text-lg font-semibold tracking-tight text-foreground leading-tight">
              Начни здесь
            </h2>
            <p className="text-xs sm:text-sm text-muted-foreground mt-0.5 truncate">
              {section.title}
            </p>
          </div>
        </div>
      </div>

      <Card className="relative overflow-hidden border-primary/25 bg-gradient-to-br from-primary/[0.07] via-primary/[0.03] to-transparent py-2 px-2 sm:py-3 sm:px-3 gap-0 shadow-sm">
        <div
          className="absolute -top-24 -left-24 size-64 rounded-full bg-primary/[0.1] blur-3xl pointer-events-none"
          aria-hidden="true"
        />
        <ul className="relative space-y-0.5 sm:space-y-1">
          {section.items.map((item) => {
            const Icon = ITEM_ICON[item.itemType];
            const tone = ITEM_TONE[item.itemType] ?? ITEM_TONE.Lesson;
            const typeLabel = ITEM_TYPE_LABEL[item.itemType];
            return (
              <li key={item.id}>
                <Link
                  href={getCourseItemHref(courseSlug, item.itemType, item.id)}
                  prefetch={false}
                  className="group/item flex items-center gap-3 sm:gap-4 rounded-xl px-2.5 py-2.5 sm:px-4 sm:py-4 hover:bg-primary/[0.08] transition-colors duration-200"
                >
                  {Icon ? (
                    <div
                      className={cn(
                        "size-9 sm:size-12 rounded-lg sm:rounded-xl flex items-center justify-center shrink-0 border",
                        tone.bg,
                        tone.border,
                      )}
                    >
                      <Icon size={16} className={cn("sm:hidden", tone.text)} />
                      <Icon size={20} className={cn("hidden sm:block", tone.text)} />
                    </div>
                  ) : null}
                  <div className="min-w-0 flex-1">
                    <p className="text-[13px] sm:text-sm font-semibold text-foreground leading-snug group-hover/item:text-primary transition-colors line-clamp-1">
                      {item.title}
                    </p>
                    <div className="flex items-center gap-1.5 sm:gap-2 mt-0.5 sm:mt-1 text-[10px] sm:text-xs text-muted-foreground">
                      {typeLabel ? (
                        <span className="uppercase tracking-[0.12em]">{typeLabel}</span>
                      ) : null}
                      {item.materialKind === "VIDEO" && !!item.durationSeconds ? (
                        <>
                          <span className="text-muted-foreground/30">·</span>
                          <span className="tabular-nums">
                            {formatDurationSecondsHuman(item.durationSeconds)}
                          </span>
                        </>
                      ) : null}
                    </div>
                  </div>
                  <Icons.arrowRight
                    size={16}
                    className="text-muted-foreground/30 group-hover/item:text-primary group-hover/item:translate-x-1 transition-all duration-300 shrink-0"
                  />
                </Link>
              </li>
            );
          })}
        </ul>
      </Card>
    </section>
  );
}
