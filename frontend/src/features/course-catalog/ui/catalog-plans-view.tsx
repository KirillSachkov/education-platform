"use client";

import type { PublicPlanDto } from "@/entities/access-plan";
import { CourseCatalogCard, type CourseCatalogDto } from "@/entities/course";
import {
  getOfferTypeBadge,
  OFFER_TYPE_VISUALS,
  type PlanOfferType,
} from "@/shared/config/offer-type";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { buildPlanFormatByCourseId, buildPlansByCourseId } from "../lib/catalog-plan-format";
import { CoursePlansDropdown } from "./course-plans-dropdown";

interface CatalogPlansViewProps {
  courses: CourseCatalogDto[];
  plans: PublicPlanDto[];
  enrollmentByCourseId: Map<string, { progressPercent: number }>;
}

/**
 * Группированный «По планам» вид каталога (#418, Phase B1 → B2):
 *   1. Full-access блок (gold) — шапка из FULL_ALL-плана + грид «обычных» курсов.
 *   2. Секция «Интенсивы и марафоны» (teal/cyan) — продаются отдельно.
 *
 * **B2:** драйвер группировки — НЕ `Course.Kind`, а offer-type покрывающего плана.
 * Для каждого курса `format` вычисляется из covering-планов: если среди них есть план
 * с `offerType ∈ {INTENSIVE, MARATHON}` → этот формат (приоритет INTENSIVE), иначе курс
 * относится к full-access блоку (COURSE). Так каталог отражает, КАК автор продаёт курс,
 * а не его технический тип.
 *
 * На каждой карточке — дропдаун «входит в планы» с deep-link на план + бейдж формата,
 * вычисленный из той же `format`-функции (когерентность секции и бейджа). Карта
 * `course → покрывающие планы` строится ОДИН раз на уровне view (батч, не per-card
 * запрос) и раздаётся в карточки готовыми массивами через `plansSlot`.
 *
 * Пустые секции скрываются. Если у платформы нет FULL_ALL-плана — gold-блок не
 * рендерится (его шапка строится из этого плана).
 */
export function CatalogPlansView({ courses, plans, enrollmentByCourseId }: CatalogPlansViewProps) {
  // Один проход: для каждого курса — список покрывающих его планов. Раздаём готовые
  // массивы в карточки, чтобы каждая не фильтровала planы заново. React Compiler
  // мемоизирует тело компонента — ручной useMemo не нужен.
  const plansByCourseId = buildPlansByCourseId(courses, plans);

  // format(course): INTENSIVE/MARATHON если среди covering-планов есть такой оффер
  // (приоритет INTENSIVE), иначе COURSE — курс уходит в full-access блок.
  const formatByCourseId = buildPlanFormatByCourseId(courses, plansByCourseId);

  const fullPlan = plans.find((p) => p.tier === "FULL_ALL");
  const regularCourses = courses.filter((c) => formatByCourseId.get(c.id) === "COURSE");
  const intensiveAndMarathon = courses.filter((c) => {
    const format = formatByCourseId.get(c.id);
    return format === "INTENSIVE" || format === "MARATHON";
  });
  const hasIntensive = intensiveAndMarathon.some((c) => formatByCourseId.get(c.id) === "INTENSIVE");
  const hasMarathon = intensiveAndMarathon.some((c) => formatByCourseId.get(c.id) === "MARATHON");

  const renderCard = (course: CourseCatalogDto, priority = false) => {
    const covering = plansByCourseId.get(course.id) ?? [];
    const format = formatByCourseId.get(course.id) ?? "COURSE";
    return (
      <div key={course.id} className="h-full">
        <CourseCatalogCard
          course={course}
          enrollment={enrollmentByCourseId.get(course.id)}
          priority={priority}
          // Бейдж формата согласован с секцией: COURSE → null (нейтрально, без
          // бейджа), INTENSIVE/MARATHON → teal/cyan. Передаём всегда (даже null),
          // чтобы карточка не падала обратно на course.kind и не противоречила секции.
          formatBadge={getOfferTypeBadge(format)}
          plansSlot={covering.length > 0 ? <CoursePlansDropdown coveringPlans={covering} /> : null}
        />
      </div>
    );
  };

  return (
    <div className="space-y-10">
      {fullPlan && regularCourses.length > 0 && (
        <FullAccessBlock>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-4 sm:gap-5">
            {regularCourses.map((course, index) => renderCard(course, index < 4))}
          </div>
        </FullAccessBlock>
      )}

      {intensiveAndMarathon.length > 0 && (
        <section className="space-y-4">
          <div className="flex items-start gap-3">
            <span className="mt-0.5 inline-flex size-9 shrink-0 items-center justify-center rounded-xl bg-teal-500/10 text-teal-600 dark:text-teal-300">
              <Icons.energy className="size-5" />
            </span>
            <div className="min-w-0">
              <div className="flex flex-wrap items-center gap-2">
                <h2 className="text-lg font-bold tracking-tight sm:text-xl">
                  Интенсивы и марафоны
                </h2>
                {hasIntensive && <OfferChip offerType="INTENSIVE" />}
                {hasMarathon && <OfferChip offerType="MARATHON" />}
              </div>
              <p className="mt-0.5 max-w-2xl text-sm text-muted-foreground">
                Короткие форматы — берутся отдельно. Тоже входят в полный доступ .NET Fullstack.
              </p>
            </div>
          </div>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-4 sm:gap-5">
            {intensiveAndMarathon.map((course) => renderCard(course))}
          </div>
        </section>
      )}
    </div>
  );
}

/**
 * Секция курсов, входящих в полный доступ. Большую gold-плашку с ценой/CTA убрали
 * (#437): цену полного доступа юзер видит на /pricing, в каталоге достаточно
 * карточек с бейджами «входит в план». Остаётся лёгкая шапка-подпись над гридом.
 */
function FullAccessBlock({ children }: { children: React.ReactNode }) {
  return (
    <section className="space-y-3">
      <h3 className="text-sm font-semibold tracking-tight text-muted-foreground">
        Что входит в полный доступ .NET Fullstack
      </h3>
      {children}
    </section>
  );
}

/** Teal/cyan чип формата в шапке секции — акцент из единого `OFFER_TYPE_VISUALS`. */
function OfferChip({ offerType }: { offerType: PlanOfferType }) {
  const visual = OFFER_TYPE_VISUALS[offerType];
  if (!visual) return null;
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-md px-2 py-0.5 text-[11px] font-semibold",
        visual.badgeClass,
      )}
    >
      {visual.badgeLabel}
    </span>
  );
}
