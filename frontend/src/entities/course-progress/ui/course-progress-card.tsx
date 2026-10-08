"use client";

import type { CourseLearningSummaryDto } from "../types";
import { pluralize } from "@/shared/lib/pluralize";
import { ProgressBar } from "@/shared/ui/components/progress-bar";
import { Card } from "@/shared/ui/kit/card";
import { CheckCircle2 } from "lucide-react";
import { ENTITY_ICONS, ENTITY_COLORS } from "@/shared/config/entity-icons";

interface CourseProgressCardProps {
  summary: CourseLearningSummaryDto;
}

export function CourseProgressCard({ summary }: CourseProgressCardProps) {
  return (
    <Card className="p-6 gap-5">
      <div className="flex items-end justify-between gap-4">
        <div>
          <p className="text-sm font-semibold">Ваш прогресс</p>
          <p className="text-sm text-muted-foreground mt-1">
            {summary.completedItems} из {summary.totalItems}{" "}
            {pluralize(summary.totalItems, "элемента", "элементов", "элементов")} завершено
          </p>
        </div>
        <div className="text-right shrink-0">
          <p className="text-3xl font-bold text-primary">
            {summary.progressPercent ?? 0}%
          </p>
        </div>
      </div>

      <ProgressBar value={summary.progressPercent ?? 0} />

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <div className="rounded-xl border bg-secondary/40 px-4 py-3">
          <div className="flex items-center gap-2 text-muted-foreground text-xs mb-1">
            <ENTITY_ICONS.module size={13} className={ENTITY_COLORS.module} />
            Модули
          </div>
          <p className="text-sm font-semibold">
            {summary.modulesCompleted}/{summary.totalModules}
          </p>
        </div>

        <div className="rounded-xl border bg-secondary/40 px-4 py-3">
          <div className="flex items-center gap-2 text-muted-foreground text-xs mb-1">
            <ENTITY_ICONS.lesson size={13} className={ENTITY_COLORS.lesson} />
            Материалы
          </div>
          <p className="text-sm font-semibold">
            {summary.materialsViewed}/{summary.materialsTotal}
          </p>
        </div>

        <div className="rounded-xl border bg-secondary/40 px-4 py-3">
          <div className="flex items-center gap-2 text-muted-foreground text-xs mb-1">
            <ENTITY_ICONS.issue size={13} className={ENTITY_COLORS.issue} />
            Задачи
          </div>
          <p className="text-sm font-semibold">
            {summary.issuesCompleted}/{summary.issuesTotal}
          </p>
        </div>

        <div className="rounded-xl border bg-secondary/40 px-4 py-3">
          <div className="flex items-center gap-2 text-muted-foreground text-xs mb-1">
            <CheckCircle2 size={13} className="text-green" />
            Завершено
          </div>
          <p className="text-sm font-semibold">
            {summary.completedItems}/{summary.totalItems}
          </p>
        </div>
      </div>
    </Card>
  );
}
