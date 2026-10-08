"use client";

import { useState } from "react";
import Link from "next/link";
import type { CourseAccessLevel } from "@/entities/course";
import type { CourseLearningStateDto } from "@/entities/course-progress";
import { routes } from "@/shared/config/routes";
import { pluralizeRu, RU_PLURALS } from "@/shared/lib/pluralize";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { CurriculumSectionCard } from "./curriculum-section-card";
import type { ProgramPreviewSection } from "../lib/program-preview";

interface CourseProgramPreviewProps {
  sections: ProgramPreviewSection[];
  courseSlug: string;
  learningState: CourseLearningStateDto | null | undefined;
  accessLevel: CourseAccessLevel;
  gettingStartedModuleId: string | null;
  lastPositionItemId?: string | null;
  currentPath?: string | null;
  className?: string;
}

// Сколько модулей видно сразу и на сколько раскрывает каждый клик «Показать ещё» —
// чтобы обзор курса не растягивался вниз на десятки модулей (#662).
const PAGE_SIZE = 10;

/**
 * «Следующие шаги» на обзоре курса — ВСЯ программа курса по порядку (#662), с
 * пагинацией: первые {@link PAGE_SIZE} модулей + кнопка «Показать ещё». Карточки
 * модулей переиспользуют тот же `CurriculumSectionCard`, что и страница «Программа»
 * (#629): бейдж-номер, прогресс-диал, раскрытие списка элементов. Активный модуль
 * раскрыт по умолчанию (если попал в видимую часть), как на странице программы.
 */
export function CourseProgramPreview({
  sections,
  courseSlug,
  learningState,
  accessLevel,
  gettingStartedModuleId,
  lastPositionItemId,
  currentPath,
  className,
}: CourseProgramPreviewProps) {
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);

  if (sections.length === 0) return null;

  const visibleSections = sections.slice(0, visibleCount);
  const remaining = sections.length - visibleSections.length;

  return (
    <section className={className}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-[10px] font-semibold uppercase tracking-[0.16em] text-primary/80">
            Следующие шаги
          </p>
          <h2 className="mt-1 text-lg font-semibold tracking-tight text-foreground">
            Программа курса
          </h2>
        </div>
        <Link
          href={routes.courseProgram(courseSlug)}
          className="inline-flex min-h-9 shrink-0 items-center gap-1.5 rounded-lg px-2.5 text-sm font-medium text-muted-foreground transition-colors hover:text-primary"
        >
          Вся программа
          <Icons.arrowRight size={15} />
        </Link>
      </div>

      <div className="space-y-2">
        {visibleSections.map((preview) => (
          <CurriculumSectionCard
            key={preview.section.id}
            section={preview.section}
            sectionNumber={preview.sectionNumber}
            sectionKind={
              preview.section.id === gettingStartedModuleId ? "getting-started" : "module"
            }
            itemFilter="all"
            learningState={learningState}
            accessLevel={accessLevel}
            courseSlug={courseSlug}
            defaultOpen={preview.isActive}
            currentPath={currentPath}
            lastPositionItemId={lastPositionItemId}
          />
        ))}
      </div>

      {remaining > 0 && (
        <div className="mt-3 flex justify-center">
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 gap-1.5 rounded-lg px-4 text-sm font-medium text-muted-foreground hover:text-primary"
            onClick={() => setVisibleCount((count) => count + PAGE_SIZE)}
          >
            <Icons.chevronDown size={15} />
            Показать ещё {remaining} {pluralizeRu(remaining, RU_PLURALS.module)}
          </Button>
        </div>
      )}
    </section>
  );
}
