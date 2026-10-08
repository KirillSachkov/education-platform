"use client";

import type { CourseCatalogDto, CoursePricingBlock } from "../types";
import { Badge } from "@/shared/ui/kit/badge";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { getCourseKindBadge } from "@/shared/config/course-kind";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { AuthorCredit } from "@/shared/ui/components/author-credit";
import { ContentImage } from "@/shared/ui/components";
import { courseLandingQueryOptions } from "../api";
import { useQueryClient } from "@tanstack/react-query";
import { Icons } from "@/shared/ui/icons";
import Link from "next/link";
import { useRoles } from "@/shared/auth";

interface CourseCatalogCardProps {
  course: CourseCatalogDto;
  enrollment?: { progressPercent: number };
  /** Set true for the first 1-2 cards above the fold to hint the browser to fetch the cover eagerly. */
  priority?: boolean;
  /**
   * Опциональный слот под ценовым блоком — например дропдаун «входит в планы».
   * Рендерится как sibling ПОД карточкой-`<a>` (не внутри неё), чтобы не вкладывать
   * интерактив/ссылки в anchor. Карточка не знает про планы — слот собирает caller.
   */
  plansSlot?: React.ReactNode;
  /**
   * Опциональный override бейджа формата поверх обложки. Передаётся, когда секция
   * каталога группирует курсы по offer-type покрывающего плана (#418/#425) — чтобы
   * бейдж карточки совпадал с секцией, а не противоречил `course.kind`.
   * `undefined` (проп опущен) → фоллбек на `getCourseKindBadge(course.kind)`;
   * `null` → бейдж явно скрыт.
   */
  formatBadge?: { label: string; class: string } | null;
}

export function CourseCatalogCard({
  course,
  enrollment,
  priority = false,
  plansSlot,
  formatBadge,
}: CourseCatalogCardProps) {
  const queryClient = useQueryClient();

  const href = routes.courseOverview(course.slug);
  const isEnrolled = !!enrollment;
  // formatBadge передан (даже null) → используем его; иначе фоллбек на course.kind.
  const kindBadge = formatBadge !== undefined ? formatBadge : getCourseKindBadge(course.kind);
  // Автор/админ имеет полный доступ ко всему контенту (entitlement bypass по роли).
  // Без этого на своём же курсе показывается «Пробный доступ» + цена. Роль —
  // синхронно из сессии, без запроса на карточку (каталог = много карточек).
  const { isAtLeast } = useRoles();
  const hasFullAccess = isAtLeast("platform-author");
  // Полный доступ к курсу: роль автора/админа ИЛИ покрытие планом (бэкенд isAccessible).
  // Без второго условия full-access студент видел ценник на уже доступный курс (#418).
  const hasAccess = hasFullAccess || course.isAccessible;

  return (
    <div className="flex h-full flex-col">
      <Link
        href={href}
        className="block group flex-1"
        onMouseEnter={() => queryClient.prefetchQuery(courseLandingQueryOptions(course.id))}
      >
        <Card
          className={cn(
            "overflow-clip gap-0 py-0 hover:border-primary/40 hover:shadow-lg hover:shadow-primary/[0.06] hover:-translate-y-0.5 transition-all duration-200 h-full",
            // Новый курс — мягкое золотое свечение вместо отдельного бейджа «New».
            course.isNew &&
              !isEnrolled &&
              "border-amber-300/30 shadow-[0_0_22px_-6px_rgba(251,191,36,0.45)] hover:border-amber-300/50 hover:shadow-[0_0_30px_-4px_rgba(251,191,36,0.6)]",
          )}
        >
          <div className="relative aspect-video overflow-clip">
            {course.imageUrl ? (
              <ContentImage
                src={course.imageUrl}
                alt={course.title}
                fill
                loading={priority ? "eager" : "lazy"}
                fetchPriority={priority ? "high" : undefined}
                sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 33vw"
                className="object-cover group-hover:scale-[1.03] transition-transform duration-500 ease-out"
              />
            ) : (
              <div className="w-full h-full bg-gradient-to-br from-primary/20 via-primary/10 to-secondary" />
            )}
            <div className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/25 to-transparent" />
            {isEnrolled && (
              <Badge className="absolute top-2 left-2 bg-primary/90 text-primary-foreground border-0 text-xs gap-1">
                <Icons.success className="size-3" />
                Вы записаны
              </Badge>
            )}
            {kindBadge && (
              <Badge className={cn("absolute top-2 right-2 border-0 text-xs", kindBadge.class)}>
                {kindBadge.label}
              </Badge>
            )}
            <div className="absolute inset-x-0 bottom-0 p-3 sm:p-4">
              <h3
                className="line-clamp-2 text-sm sm:text-base font-bold leading-tight text-white drop-shadow-sm"
                style={{ viewTransitionName: `course-${course.id}-title` }}
              >
                {course.title}
              </h3>
            </div>
          </div>
          <CardContent className="p-3 sm:p-4 flex flex-col flex-1">
            <p className="text-xs text-muted-foreground mb-2 line-clamp-2 flex-1 leading-relaxed">
              {course.description}
            </p>
            {course.authorDisplayName && (
              <AuthorCredit
                name={course.authorDisplayName}
                avatarUrl={course.authorAvatarUrl}
                className="mb-3"
              />
            )}
            {isEnrolled ? (
              <div className="space-y-1.5">
                <div className="flex items-center justify-between gap-2 text-xs">
                  <span className="text-primary font-semibold">Продолжить обучение</span>
                  <span className="tabular-nums text-muted-foreground">
                    {enrollment.progressPercent}%
                  </span>
                </div>
                <div className="h-1 w-full overflow-hidden rounded-full bg-muted">
                  <div
                    className="h-full bg-primary transition-all"
                    style={{ width: `${enrollment.progressPercent}%` }}
                  />
                </div>
              </div>
            ) : hasAccess ? (
              <span className="inline-flex items-center gap-1 text-xs font-semibold text-primary">
                Открыть курс
                <Icons.arrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" />
              </span>
            ) : course.pricing ? (
              <div className="flex items-center justify-end gap-2">
                <CoursePriceRow pricing={course.pricing} />
              </div>
            ) : course.kind === "COURSE" ? (
              // Обычный курс без своего плана доступен только в составе полного доступа.
              <span className="inline-flex items-center rounded-full border border-amber-300/30 bg-amber-400/10 px-2 py-0.5 text-[10px] font-medium text-amber-200/90">
                Входит в полный доступ
              </span>
            ) : (
              // Интенсивы/марафоны продаются отдельно — НЕ маркируем их как часть
              // полного доступа. Цена рендерится в ветке `course.pricing` выше, когда
              // у интенсива привязан собственный план; пока плана нет — нейтральный CTA.
              <span className="inline-flex items-center gap-1 text-xs font-semibold text-primary">
                Подробнее
                <Icons.arrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" />
              </span>
            )}
          </CardContent>
        </Card>
      </Link>
      {/* Бейдж «входит в план» — выровнен по левому контентному краю карточки
          (p-3/sm:p-4), а не по её внешней рамке, чтобы не уезжал влево (#437). */}
      {plansSlot ? <div className="mt-2 px-3 sm:px-4">{plansSlot}</div> : null}
    </div>
  );
}

const RUB_FORMATTER = new Intl.NumberFormat("ru-RU");

function formatPrice(cents: number, currency: string): string {
  const symbol = currency === "RUB" ? "₽" : currency;
  return `${RUB_FORMATTER.format(Math.floor(cents / 100))} ${symbol}`;
}

/**
 * Цена COURSE-плана на карточке курса. При активной акции — эффективная цена +
 * зачёркнутая старая + бейдж «−N%» (значения считает бэкенд в read-time).
 */
function CoursePriceRow({ pricing }: { pricing: CoursePricingBlock }) {
  const effective = formatPrice(pricing.effectivePriceCents, pricing.currency);

  if (pricing.promotionActive) {
    return (
      <span className="flex items-center gap-1.5">
        <span className="text-sm font-bold text-foreground">{effective}</span>
        <s className="text-[11px] text-muted-foreground">
          {formatPrice(pricing.priceCents, pricing.currency)}
        </s>
        {pricing.discountPercent != null ? (
          <span className="rounded-full bg-rose-500/15 px-1.5 py-0.5 text-[10px] font-semibold text-rose-600 dark:text-rose-400">
            −{pricing.discountPercent}%
          </span>
        ) : null}
      </span>
    );
  }

  return <span className="text-sm font-bold text-foreground">{effective}</span>;
}
