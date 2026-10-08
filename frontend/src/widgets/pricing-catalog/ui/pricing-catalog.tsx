"use client";

import { useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { motion, useInView } from "framer-motion";
import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import {
  myGrantsQueryOptions,
  publicPlansQueryOptions,
  upgradeQuoteQueryOptions,
  type PublicPlanDto,
  type UpgradeQuoteDto,
} from "@/entities/access-plan";
import { hasFullAccessGrant, sortPublicPlans } from "@/entities/access-plan";
import {
  catalogQueryOptions,
  platformContentStatsQueryOptions,
  type CourseCatalogDto,
} from "@/entities/course";
import {
  COURSE_KIND_LABELS,
  getCourseKindBadge,
  type CourseKind,
} from "@/shared/config/course-kind";
import { routes } from "@/shared/config/routes";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { ContentImage } from "@/shared/ui/components";
import { Card } from "@/shared/ui/kit/card";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/shared/ui/kit/accordion";
import { cn } from "@/shared/lib/css";
import { pluralizeRu, RU_PLURALS } from "@/shared/lib/pluralize";
import { useHashScroll } from "@/shared/hooks/use-hash-scroll";
import { useIsAuthenticated } from "@/shared/auth/use-is-authenticated";
import {
  COMPARISON_GROUPS,
  PLANS as STATIC_PLANS,
  PRICING_CTA_HREF,
  PRICING_FAQ,
  type PlanCard,
  type PlanCardKind,
} from "@/shared/config/landing-plans";
import { mapPublicPlanToCard } from "../lib/map-public-plan";
import { personalPriceParts } from "../lib/personal-price";
import { PlanIncludedContent } from "./plan-included-content";
import { UpgradeCreditBreakdown } from "./upgrade-credit-breakdown";
import { BuyPlanButton, useResumePricingCheckout } from "@/features/buy-plan";
import { useBillingEnabled } from "@/entities/billing-config";
import { useTrackGrowthView } from "@/shared/analytics";

interface PricingCatalogProps {
  /**
   * SSR pre-fetched plans — передаются как initialData в useQuery.
   * Сразу рисуют real cards без skeleton'а на первом рендере.
   */
  initialPlans?: PublicPlanDto[];
}

/**
 * Иконки платёжных систем — текстовые badge'и, чтобы не подключать тяжёлые
 * SVG иконки. Заменим на реальные иконки когда подключим payment provider.
 */
const PAYMENT_METHODS = ["МИР", "СБП", "T-Pay", "SberPay", "Долями"];

// ---------------------------------------------------------------------------
// PricingCatalog — full pricing page композиция
// Hero + крупные карточки + сравнительная таблица + FAQ + trust row.
// Theme-tokens (bg-background, text-foreground, bg-card, ...) — соответствует
// общему стилю платформы. Highlighted-карточка с акцентом primary.
// ---------------------------------------------------------------------------

export function PricingCatalog({ initialPlans }: PricingCatalogProps = {}) {
  useTrackGrowthView(
    { name: "catalog_view", properties: { catalog_kind: "pricing" } },
    "catalog:pricing",
  );
  const isAuthenticated = useIsAuthenticated();
  const billingEnabled = useBillingEnabled();
  // initialData — SSR pre-fetched plans, чтобы первый рендер показывал real cards
  // вместо skeleton'а. Тип в queryFn: { result, error, isError } envelope, поэтому
  // оборачиваем в тот же shape.
  const { data: backendPlans, isLoading } = useQuery({
    ...publicPlansQueryOptions(),
    initialData: initialPlans
      ? {
          result: initialPlans,
          error: null,
          isError: false,
          timeGenerated: new Date().toISOString(),
        }
      : undefined,
    // initialData без staleTime считается сразу stale → мгновенный refetch на
    // mount дублировал SSR-загрузку (#512). Минута свежести достаточна для цен.
    staleTime: 60_000,
  });

  useResumePricingCheckout({
    publicPlans: backendPlans,
    isLoading,
    isAuthenticated,
    billingEnabled,
  });

  // Deep-link к плану: /pricing/[slug] редиректит на /pricing#<slug>, якоря — id={plan.id}
  // (= slug, см. map-public-plan). Перевыравниваемся после загрузки планов и догрузки
  // ленивых витрин, иначе нижний план «открывается с начала страницы» (#640).
  useHashScroll(!isLoading);

  // Мои активные plan-grants — для отметки «У вас уже есть этот план» на карточках.
  // Без этого юзер мог бы повторно нажать «Получить» на купленном плане.
  const { data: myGrants } = useQuery({
    ...myGrantsQueryOptions(),
    enabled: isAuthenticated,
  });
  const ownedPlanIds = new Set<string>(
    (myGrants ?? [])
      .filter((g): g is typeof g & { planId: string } => g.status === "ACTIVE" && Boolean(g.planId))
      .map((g) => g.planId),
  );
  // Юзер с активным полным доступом (lifetime / month-access) не должен видеть
  // сегмент «На месяц» в hero — backend всё равно отклонит покупку
  // (order.nothing_to_pay: существующий grant 100% кредитует trial). #580/#595
  const hasFullAccess = hasFullAccessGrant(myGrants ?? []);

  // Predicate: используем backend-данные если запрос успешен И вернул что-то.
  // Иначе fallback на hardcoded (для лендингового сценария, dev без AccessService и т.д.)
  const plans: PlanCard[] = (() => {
    if (isLoading) return [];
    const source =
      backendPlans && backendPlans.length > 0
        ? sortPublicPlans(backendPlans).map(mapPublicPlanToCard)
        : STATIC_PLANS;
    // Тренажёр продаётся отдельной страницей /trainer/pro (#623) — в платформенный
    // каталог планов подписку тренажёра не подмешиваем.
    return source.filter((plan) => plan.offerType !== "TRAINER_PRO");
  })();

  // Comparison-таблица показывается только для hardcoded planов — там
  // согласованные ключи между PLANS.comparisonKeys и COMPARISON_GROUPS.rows.key.
  // Для backend-driven планов структура comparison другая; покажем только карточки.
  const showComparison = !backendPlans || backendPlans.length === 0;

  return (
    <div className="bg-background text-foreground">
      <PricingHero />
      <PricingCards plans={plans} ownedPlanIds={ownedPlanIds} hasFullAccess={hasFullAccess} />
      {showComparison && <PricingComparison plans={STATIC_PLANS} />}
      <PricingFaq />
      <PricingTrustAndCta />
    </div>
  );
}

// ---------------------------------------------------------------------------
// HERO
// ---------------------------------------------------------------------------

function PricingHero() {
  return (
    <section className="relative pt-16 pb-8 md:pt-20 md:pb-12 lg:pt-24">
      <div className="mx-auto max-w-5xl px-6 text-center">
        <motion.p
          className="text-xs font-medium uppercase tracking-[0.2em] text-primary/80"
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          Доступ к обучению .NET Fullstack
        </motion.p>
        <motion.h1
          className="mt-3 text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl"
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.05, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          Выберите доступ к .NET Fullstack
        </motion.h1>
        <motion.p
          className="mx-auto mt-4 max-w-2xl text-sm text-muted-foreground sm:text-base md:text-lg"
          initial={{ opacity: 0, y: 16 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, delay: 0.1, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          Полный доступ открывает всю программу .NET Fullstack: оба уровня, задания и проекты,
          AI-ревью PR, закрытый чат и поддержку автора. Оплата один раз, без подписки.
        </motion.p>
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------
// PLAN SECTIONS — full-access hero (две колонки) + продуктовые карточки
// курсов/интенсивов/марафонов (привязанный курс показан КРУПНО — «вот именно
// это вы покупаете») + подписки. Группировка по tier; пустые секции скрыты.
// Issue #384 redesign.
// ---------------------------------------------------------------------------

type PlanGroupKey = "full" | "course" | "subscription" | "other";

function planGroupKey(plan: PlanCard): PlanGroupKey {
  switch (plan.tier) {
    case "FULL_ALL":
    case "LEARN_ALL":
      return "full";
    case "COURSE":
      return "course";
    case "SUBSCRIPTION":
      return "subscription";
    default:
      return "other";
  }
}

/**
 * Лейбл цикла подписки для ценника: «/ мес» при ~месячном интервале, иначе
 * «/ N дн.». Подписочные планы (#614) показывают cadence рядом с ценой —
 * «₽X / мес» с автопродлением.
 */
function cadenceLabel(termRecurringDays?: number | null): string | null {
  if (termRecurringDays == null || termRecurringDays <= 0) return null;
  return termRecurringDays <= 31 ? "/ мес" : `/ ${termRecurringDays} дн.`;
}

/**
 * Bundle-план (#404) — COURSE-план, который покрывает несколько курсов.
 * Детектим по `includedCourses` (полный список с backend'а) или `courseIds`.
 */
function isBundlePlan(plan: PlanCard): boolean {
  const included = plan.includedCourses?.length ?? 0;
  const ids = plan.courseIds?.length ?? 0;
  return Math.max(included, ids) > 1;
}

/**
 * «Полный доступ на месяц» (#580/#595) — платный план с временным полным доступом.
 * Детектим по `trialDurationDays > 0`. Такой план не рендерится отдельной карточкой:
 * он становится сегментом «На месяц» внутри hero бессрочного «Полного доступа» (#595).
 */
function isTrialCard(plan: PlanCard): boolean {
  return (plan.trialDurationDays ?? 0) > 0;
}

function PricingCards({
  plans,
  ownedPlanIds,
  hasFullAccess,
}: {
  plans: PlanCard[];
  ownedPlanIds: Set<string>;
  hasFullAccess: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, margin: "-8%" });

  // Каталог курсов — обложки + тип (курс/интенсив/марафон) для продуктовых
  // карточек COURSE-планов. react-query дедупит с PlanIncludedContent (тот же ключ).
  // Нужен только course/bundle-карточкам — без COURSE-планов 48-курсовый каталог
  // не запрашиваем вовсе (#512).
  const hasCoursePlans = plans.some((plan) => planGroupKey(plan) === "course");
  const { data: catalog } = useInfiniteQuery({
    ...catalogQueryOptions({ limit: 48 }),
    enabled: hasCoursePlans,
  });
  const courseById = new Map<string, CourseCatalogDto>(
    (catalog?.items ?? []).map((c) => [c.id, c] as const),
  );

  if (plans.length === 0) {
    // loading — skeleton'ы той же композиции чтобы layout не прыгал
    return (
      <section className="relative pb-16 md:pb-24">
        <div className="mx-auto max-w-6xl px-6">
          <div className="h-64 animate-pulse rounded-3xl border border-border bg-muted/30" />
          <div className="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <div
                key={i}
                className="h-[360px] animate-pulse rounded-2xl border border-border bg-muted/30"
              />
            ))}
          </div>
        </div>
      </section>
    );
  }

  const isOwnedFor = (plan: PlanCard) => Boolean(plan.planId && ownedPlanIds.has(plan.planId));

  // Trial-план (#580 → #595) больше не отдельная карточка/секция: единственный
  // опубликованный trial («Полный доступ на месяц») становится сегментом «На месяц»
  // в hero бессрочного «Полного доступа». Вынимаем его из tier-группировки, чтобы
  // FULL_ALL-trial не стал самостоятельным lifetime-hero.
  // Юзеру с активным полным доступом сегмент «На месяц» не показываем — покупка
  // бессмысленна (backend отклонит как order.nothing_to_pay). #580
  const trialPlan = hasFullAccess ? undefined : plans.find(isTrialCard);
  const nonTrialPlans = plans.filter((p) => !isTrialCard(p));

  const fullPlans = nonTrialPlans.filter((p) => planGroupKey(p) === "full");
  const coursePlans = nonTrialPlans.filter((p) => planGroupKey(p) === "course");
  const subPlans = nonTrialPlans.filter((p) => planGroupKey(p) === "subscription");
  const otherPlans = nonTrialPlans.filter((p) => planGroupKey(p) === "other");

  // Полный доступ: highlighted-план = крупный hero, остальные full-планы — компактные карточки.
  const heroPlan = fullPlans.find((p) => p.highlighted) ?? fullPlans[0];
  const secondaryFull = heroPlan ? fullPlans.filter((p) => p.id !== heroPlan.id) : fullPlans;

  const navItems = [
    heroPlan ? { id: "section-full", label: "Полный доступ" } : null,
    coursePlans.length > 0 ? { id: "section-course", label: "Курсы и интенсивы" } : null,
    subPlans.length > 0 ? { id: "section-subscription", label: "Подписки" } : null,
  ].filter((x): x is { id: string; label: string } => x !== null);

  const gridFor = (n: number) =>
    cn(
      "grid gap-6",
      n === 1 && "max-w-md",
      n === 2 && "sm:grid-cols-2",
      n >= 3 && "sm:grid-cols-2 lg:grid-cols-3",
    );

  return (
    <section ref={ref} className="relative pb-16 md:pb-24">
      <div className="mx-auto max-w-6xl px-6">
        {navItems.length > 1 && <SectionNav items={navItems} />}

        <div className="mt-8 space-y-16 md:mt-10 md:space-y-24">
          {heroPlan && (
            <SectionHeader
              id="section-full"
              icon={<Icons.crown className="size-5" />}
              accent="gold"
              title="Полный доступ к обучению .NET Fullstack"
              subtitle="Один доступ ко всему направлению: текущим и будущим курсам, заданиям, ревью и сообществу."
            >
              <FullAccessHero
                plan={heroPlan}
                trialPlan={trialPlan}
                isOwned={isOwnedFor(heroPlan)}
                isTrialOwned={trialPlan ? isOwnedFor(trialPlan) : false}
                inView={inView}
              />
              {secondaryFull.length > 0 && (
                <div className="mt-6 grid gap-5 sm:grid-cols-2">
                  {secondaryFull.map((plan, i) => (
                    <GenericPlanCard
                      key={plan.id}
                      plan={plan}
                      index={i}
                      inView={inView}
                      isOwned={isOwnedFor(plan)}
                      compact
                    />
                  ))}
                </div>
              )}
            </SectionHeader>
          )}

          {coursePlans.length > 0 && (
            <SectionHeader
              id="section-course"
              icon={<Icons.layers className="size-5" />}
              accent="teal"
              title="Курсы, интенсивы и марафоны"
              subtitle="Нужен один курс — бери его. Всё это уже входит в полный доступ выше; отдельная покупка имеет смысл, если хочешь начать с одного и докупить разницу позже."
            >
              <div className={gridFor(coursePlans.length)}>
                {coursePlans.map((plan, i) =>
                  isBundlePlan(plan) ? (
                    <BundlePlanCard
                      key={plan.id}
                      plan={plan}
                      index={i}
                      inView={inView}
                      isOwned={isOwnedFor(plan)}
                      courseById={courseById}
                      className="sm:col-span-2 lg:col-span-3"
                    />
                  ) : (
                    <ProductPlanCard
                      key={plan.id}
                      plan={plan}
                      index={i}
                      inView={inView}
                      isOwned={isOwnedFor(plan)}
                      course={plan.courseId ? courseById.get(plan.courseId) : undefined}
                    />
                  ),
                )}
              </div>
            </SectionHeader>
          )}

          {subPlans.length > 0 && (
            <SectionHeader
              id="section-subscription"
              icon={<Icons.calendar className="size-5" />}
              accent="teal"
              title="Подписки на обучение"
              subtitle="Доступ к материалам направления на время действия подписки."
            >
              <div className={gridFor(subPlans.length)}>
                {subPlans.map((plan, i) => (
                  <GenericPlanCard
                    key={plan.id}
                    plan={plan}
                    index={i}
                    inView={inView}
                    isOwned={isOwnedFor(plan)}
                  />
                ))}
              </div>
            </SectionHeader>
          )}

          {otherPlans.length > 0 && (
            <div className={cn("mx-auto", gridFor(otherPlans.length))}>
              {otherPlans.map((plan, i) => (
                <GenericPlanCard
                  key={plan.id}
                  plan={plan}
                  index={i}
                  inView={inView}
                  isOwned={isOwnedFor(plan)}
                />
              ))}
            </div>
          )}
        </div>
      </div>
    </section>
  );
}

function SectionNav({ items }: { items: { id: string; label: string }[] }) {
  return (
    <nav className="flex flex-wrap items-center gap-2">
      {items.map((it) => (
        <a
          key={it.id}
          href={`#${it.id}`}
          className="rounded-full border border-border/70 bg-muted/30 px-4 py-2 text-sm font-medium text-muted-foreground transition-colors hover:border-primary/40 hover:bg-muted/60 hover:text-foreground"
        >
          {it.label}
        </a>
      ))}
    </nav>
  );
}

function SectionHeader({
  id,
  icon,
  accent,
  title,
  subtitle,
  children,
}: {
  id: string;
  icon: React.ReactNode;
  accent: "gold" | "teal" | "violet";
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  const accentClass =
    accent === "gold"
      ? "bg-amber-400/10 text-amber-500 dark:text-amber-300"
      : accent === "violet"
        ? "bg-violet-500/10 text-violet-500 dark:text-violet-300"
        : "bg-primary/10 text-primary";
  return (
    <div id={id} className="scroll-mt-24">
      <div className="mb-6 flex items-start gap-3 md:mb-8">
        <span
          className={cn(
            "mt-0.5 inline-flex size-9 shrink-0 items-center justify-center rounded-xl",
            accentClass,
          )}
        >
          {icon}
        </span>
        <div>
          <h2 className="text-xl font-bold tracking-tight sm:text-2xl">{title}</h2>
          <p className="mt-1 max-w-2xl text-sm text-muted-foreground sm:text-[15px]">{subtitle}</p>
        </div>
      </div>
      {children}
    </div>
  );
}

/**
 * Курируемая витрина ценности полного доступа — «это полноценное обучение, а не
 * просто доступ к видео». Иконка + заголовок + пояснение (в стиле лендинга).
 *
 * Намеренно НЕ берётся из `plan.features` (плоские строки, у backend-плана могут
 * быть пустыми): это платформо-уровневое обещание флагманского `FULL_ALL`-плана,
 * стабильное между авторами. Рендерится только для `tier === FULL_ALL`
 * (см. FullAccessHero) — у LEARN_ALL (только материалы) набор был бы неверным.
 */
const FULL_ACCESS_BENEFITS: ReadonlyArray<{
  icon: IconComponent;
  title: string;
  desc: string;
}> = [
  {
    icon: Icons.graduation,
    title: "Полноценная программа обучения",
    desc: "Оба уровня .NET-пути — от основ до senior-инженера, а не набор разрозненных уроков.",
  },
  {
    icon: Icons.users,
    title: "Поддержка автора",
    desc: "Автор отвечает на вопросы по программе в закрытом чате и в заданиях.",
  },
  {
    icon: Icons.reviewSubmitted,
    title: "AI-ревью каждого PR",
    desc: "Решение задания уходит pull request'ом, AI-ревью оставляет замечания прямо в PR.",
  },
  {
    icon: Icons.target,
    title: "Много практики",
    desc: "Большой объём практических заданий и проектов на реальных бизнес-кейсах.",
  },
  {
    icon: Icons.message,
    title: "Закрытый Telegram-чат",
    desc: "Чат участников: вопросы, разборы, нетворкинг.",
  },
  {
    icon: Icons.briefcase,
    title: "Помощь с поиском работы",
    desc: "Подскажем, как упаковать опыт и где искать вакансии.",
  },
  {
    icon: Icons.document,
    title: "Резюме и портфолио",
    desc: "Разбор резюме по запросу, проекты программы в портфолио.",
  },
  {
    icon: Icons.trending,
    title: "Программа растёт вместе с вами",
    desc: "Новые курсы и материалы программы входят в доступ без доплат.",
  },
];

/**
 * TermToggle (#595) — сегмент-контрол срока полного доступа: «Навсегда / На месяц».
 * Тот же визуальный паттерн, что и `CourseKindFilter` (segmented buttons + active
 * pill). Состояние живёт в hero; компонент только рисует и репортит выбор.
 * A11y: `role="group"` + `aria-pressed`; touch-таргеты ≥44px.
 */
function TermToggle({
  value,
  onChange,
}: {
  value: "lifetime" | "month";
  onChange: (value: "lifetime" | "month") => void;
}) {
  const options: { value: "lifetime" | "month"; label: string }[] = [
    { value: "lifetime", label: "Навсегда" },
    { value: "month", label: "На месяц" },
  ];
  return (
    <div
      role="group"
      aria-label="Срок полного доступа"
      className="inline-flex w-fit items-center gap-1 rounded-lg border border-border bg-muted/50 p-1"
    >
      {options.map((option) => {
        const isActive = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={isActive}
            onClick={() => onChange(option.value)}
            className={cn(
              "min-h-[44px] rounded-md px-4 text-sm font-medium whitespace-nowrap transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1",
              isActive
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * FullAccessHero — крупная двухколоночная карточка флагманского «полного доступа».
 * Слева: сегмент-контрол срока «Навсегда / На месяц» (#595, если у автора есть
 * опубликованный trial-план) + бейдж + название + описание + крупная цена + CTA.
 * Справа: витрина «Что входит» в отдельной панели. Золотой premium-акцент.
 *
 * Hero теперь представляет ДВА плана: бессрочный `plan` (lifetime) и опциональный
 * `trialPlan` («Полный доступ на месяц»). Сегмент переключает АКТИВНЫЙ план — его
 * id, цену, CTA и платёжную кнопку. Дефолт — «Навсегда». «Что входит» и витрина
 * курсов всегда от lifetime-плана: набор контента у обоих одинаковый.
 */
function FullAccessHero({
  plan,
  trialPlan,
  isOwned,
  isTrialOwned,
  inView,
}: {
  plan: PlanCard;
  trialPlan?: PlanCard;
  isOwned: boolean;
  isTrialOwned: boolean;
  inView: boolean;
}) {
  const [term, setTerm] = useState<"lifetime" | "month">("lifetime");
  const showToggle = Boolean(trialPlan);
  const isMonth = showToggle && term === "month";
  // Активный план — то, что юзер реально покупает/CTA таргетит. На «На месяц»
  // CTA должна вести на id ИМЕННО trial-плана, не lifetime.
  const activePlan = isMonth && trialPlan ? trialPlan : plan;
  const activeOwned = isMonth ? isTrialOwned : isOwned;
  const personal = usePersonalPrice(activePlan, activeOwned);
  return (
    <motion.div
      id={plan.id}
      className="scroll-mt-24"
      initial={{ opacity: 0, y: 28 }}
      animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 28 }}
      transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
    >
      <div className="relative overflow-hidden rounded-3xl border border-primary/30 bg-gradient-to-br from-primary/[0.05] via-card to-card shadow-[0_0_60px_-22px_rgba(107,173,165,0.35)]">
        <div
          aria-hidden
          className="pointer-events-none absolute -right-24 -top-24 size-72 rounded-full bg-amber-400/10 blur-3xl"
        />
        <div className="relative grid gap-8 p-7 sm:p-9 lg:grid-cols-[1.05fr_0.95fr] lg:gap-10 lg:p-11">
          {/* LEFT — оффер + цена + CTA */}
          <div className="flex flex-col">
            {showToggle ? <TermToggle value={term} onChange={setTerm} /> : null}
            {!isMonth ? (
              <span className="mt-5 inline-flex w-fit items-center gap-1.5 rounded-full border border-amber-400/30 bg-amber-400/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-amber-600 dark:text-amber-300">
                <Icons.crown className="size-3.5" />
                {plan.badge}
              </span>
            ) : (
              // На месяц — teal duration-чип вместо золотой короны: глаз сразу
              // видит, что это временный доступ, а не бессрочный «Полный доступ».
              <span className="mt-5 inline-flex w-fit items-center gap-1.5 rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-primary">
                <Icons.clock className="size-3.5" />
                Временный доступ
              </span>
            )}
            <h3 className="mt-5 text-2xl font-bold tracking-tight sm:text-3xl xl:text-4xl">
              {isMonth ? `${plan.name} на месяц` : plan.name}
            </h3>
            <p className="mt-3 max-w-md text-sm leading-relaxed text-muted-foreground sm:text-[15px]">
              {activePlan.description}
            </p>

            {/* Цена — key={term} перезапускает лёгкий fade при свопе срока (#595). */}
            <div key={term} className="price-swap mt-7">
              {personal.strikeLabel ? (
                <div className="mb-1.5 flex items-center gap-2">
                  <s className="text-base text-muted-foreground">{personal.strikeLabel}</s>
                </div>
              ) : activePlan.originalPriceLabel && activePlan.discountPercent ? (
                <div className="mb-1.5 flex items-center gap-2">
                  <s className="text-base text-muted-foreground">{activePlan.originalPriceLabel}</s>
                  <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-xs font-semibold text-rose-600 dark:text-rose-400">
                    −{activePlan.discountPercent}%
                  </span>
                </div>
              ) : null}
              <div className="flex items-baseline gap-2">
                <div
                  className={cn(
                    "text-4xl font-bold tracking-tight sm:text-5xl xl:text-6xl",
                    // Месяц — сплошной teal (а не золотой градиент): другой цвет
                    // сигналит «другой продукт / временный», золото = «навсегда».
                    isMonth && "text-primary",
                  )}
                  style={
                    isMonth
                      ? undefined
                      : {
                          background: "linear-gradient(135deg, #C9A84C, #E8C97D)",
                          WebkitBackgroundClip: "text",
                          WebkitTextFillColor: "transparent",
                          backgroundClip: "text",
                        }
                  }
                >
                  {personal.priceLabel}
                </div>
                {isMonth ? (
                  <span className="text-base font-semibold text-primary sm:text-lg">
                    / за месяц
                  </span>
                ) : null}
              </div>
              {isMonth ? (
                <p className="mt-2 text-xs leading-relaxed text-muted-foreground sm:text-sm">
                  Месяц полного доступа — потом доплатишь разницу до полного, уплаченное в зачёт.
                </p>
              ) : (
                <p className="mt-2 text-xs text-muted-foreground sm:text-sm">
                  {activePlan.discountEndsHint ? (
                    <span className="font-medium text-rose-600 dark:text-rose-400">
                      Акция {activePlan.discountEndsHint}
                    </span>
                  ) : null}
                  {activePlan.discountEndsHint ? " · " : ""}
                  {activePlan.priceNote}
                </p>
              )}
            </div>

            {plan.tier === "FULL_ALL" ? <PlatformStatsStrip /> : null}

            <div className="mt-auto pt-7">
              <PlanPaymentBlock
                plan={activePlan}
                isOwned={activeOwned}
                primaryCta={isMonth}
                ctaLabel={isMonth ? "Попробовать месяц" : undefined}
              />
            </div>
          </div>

          {/* RIGHT — что входит */}
          <div className="rounded-2xl border border-border/60 bg-background/40 p-6 backdrop-blur-sm sm:p-7">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Что входит
            </p>
            {plan.tier === "FULL_ALL" ? (
              <>
                <p className="mt-2 text-sm font-medium text-foreground/90">
                  Полноценное обучение, а не просто доступ к видео.
                </p>
                <ul className="mt-4 space-y-3.5">
                  {FULL_ACCESS_BENEFITS.map(({ icon: Icon, title, desc }) => (
                    <li key={title} className="flex items-start gap-3">
                      <span className="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <Icon className="size-4" />
                      </span>
                      <div className="min-w-0">
                        <p className="text-sm font-medium leading-snug text-foreground">{title}</p>
                        <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">
                          {desc}
                        </p>
                      </div>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <ul className="mt-4 space-y-3">
                {plan.features.map((feature) => (
                  <li key={feature.text} className="flex items-start gap-2.5">
                    <Icons.check className="mt-0.5 size-4 shrink-0 text-primary" />
                    <span
                      className={cn(
                        "text-sm leading-relaxed",
                        feature.highlight ? "font-medium text-foreground" : "text-foreground/80",
                      )}
                    >
                      {feature.text}
                    </span>
                  </li>
                ))}
              </ul>
            )}
            {isMonth ? (
              <div className="mt-5 flex items-start gap-2.5 rounded-xl border border-primary/20 bg-primary/[0.04] px-4 py-3">
                <Icons.clock className="mt-0.5 size-4 shrink-0 text-primary" />
                <p className="text-xs leading-relaxed text-muted-foreground">
                  Полный доступ ко всему на месяц. Захочешь оставить навсегда — доплатишь разницу,
                  уплаченное за месяц пойдёт в зачёт.
                </p>
              </div>
            ) : (
              <div className="mt-5 flex items-start gap-2.5 rounded-xl border border-amber-400/20 bg-amber-400/[0.04] px-4 py-3">
                <Icons.unlocked className="mt-0.5 size-4 shrink-0 text-amber-500 dark:text-amber-300" />
                <p className="text-xs leading-relaxed text-muted-foreground">
                  Доступ открыт ко всей программе — материалы, обновления и новые курсы программы
                  включены, без ограничений.
                </p>
              </div>
            )}
          </div>
        </div>

        {/* FULL-WIDTH SHOWCASE — курсы, входящие в полный доступ (#418). Заполняет
            пустую область под основным контентом hero; фильтр по showInFullAccess.
            Сам PlanIncludedContent (variant="wide") рисует bordered-полосу и
            возвращает null, если входящих курсов нет/грузится — без пустой рамки. */}
        <PlanIncludedContent plan={plan} fullAccessOnly variant="wide" />
      </div>
    </motion.div>
  );
}

/**
 * Стат-полоса в левой колонке FullAccessHero (#437): «что уже в обучении» —
 * счётчики опубликованного контента. Заполняет пустоту между ценой и payment-
 * блоком (раньше там зиял `mt-auto`-гэп) и заодно продаёт объём полного доступа.
 * Грузится лениво; пока нет данных / всё по нулям — рендерит null (без скелета,
 * чтобы не дёргать высоту карточки).
 */
function PlatformStatsStrip() {
  const { data: stats } = useQuery(platformContentStatsQueryOptions());
  if (!stats) return null;

  const items = [
    { value: stats.coursesCount, plural: RU_PLURALS.course },
    { value: stats.materialsCount, plural: RU_PLURALS.material },
    { value: stats.issuesCount, plural: RU_PLURALS.issue },
    { value: stats.collectionsCount, plural: RU_PLURALS.collection },
  ].filter((item) => item.value > 0);

  if (items.length === 0) return null;

  return (
    <div className="mt-8 border-t border-border/50 pt-6">
      <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
        Что уже в обучении
      </p>
      <div className="mt-4 grid grid-cols-2 gap-x-4 gap-y-5 sm:grid-cols-4">
        {items.map((item) => (
          <div key={item.plural.one}>
            <div className="text-2xl font-bold tabular-nums tracking-tight text-foreground sm:text-3xl">
              {item.value}
            </div>
            <div className="mt-0.5 text-xs text-muted-foreground">
              {pluralizeRu(item.value, item.plural)}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Градиент-заглушка обложки по типу курса, когда у курса нет imageUrl. */
function KindGradient({ kind }: { kind: CourseKind }) {
  const gradient = {
    COURSE: "from-primary/30 via-primary/10 to-secondary",
    INTENSIVE: "from-teal-500/35 via-teal-500/12 to-secondary",
    MARATHON: "from-cyan-500/35 via-cyan-500/12 to-secondary",
  }[kind];
  const Glyph =
    { COURSE: Icons.graduation, INTENSIVE: Icons.energy, MARATHON: Icons.target }[kind] ??
    Icons.graduation;
  return (
    <div className={cn("relative size-full bg-gradient-to-br", gradient)}>
      <Glyph className="absolute right-4 top-4 size-12 text-white/15" />
    </div>
  );
}

/**
 * ProductPlanCard — карточка COURSE-плана: привязанный курс/интенсив/марафон
 * показан КРУПНО (обложка во всю ширину + название поверх) — «вот именно это
 * вы покупаете». Под обложкой: цена + короткий список + CTA оплаты.
 */
function ProductPlanCard({
  plan,
  index,
  inView,
  isOwned,
  course,
}: {
  plan: PlanCard;
  index: number;
  inView: boolean;
  isOwned: boolean;
  course?: CourseCatalogDto;
}) {
  const kind: CourseKind = course?.kind ?? "COURSE";
  const kindBadge = getCourseKindBadge(kind);
  const productTitle = course?.title ?? plan.name;
  const personal = usePersonalPrice(plan, isOwned);

  // Обложка кликабельна, если план привязан к конкретному курсу/интенсиву/марафону —
  // ведёт на страницу этого курса. Кликабельна только обложка (не вся карточка),
  // чтобы не вкладывать платёжные кнопки/ссылки в <a> (nested-anchor).
  const coverClass = "relative block aspect-[16/10] overflow-hidden bg-muted";
  const coverInner = (
    <>
      {course?.imageUrl ? (
        <ContentImage
          src={course.imageUrl}
          alt={productTitle}
          fill
          sizes="(max-width: 640px) 100vw, 380px"
          className="object-cover transition-transform duration-500 group-hover:scale-[1.04]"
        />
      ) : (
        <KindGradient kind={kind} />
      )}
      <div className="absolute inset-0 bg-gradient-to-t from-black/60 via-black/10 to-transparent" />
      <span
        className={cn(
          "absolute left-3 top-3 inline-flex items-center rounded-md px-2 py-1 text-[11px] font-semibold",
          kindBadge ? kindBadge.class : "bg-background/85 text-foreground backdrop-blur",
        )}
      >
        {COURSE_KIND_LABELS[kind]}
      </span>
      <div className="absolute inset-x-0 bottom-0 p-4 sm:p-5">
        <h3 className="line-clamp-2 text-lg font-bold leading-tight text-white drop-shadow-sm sm:text-xl">
          {productTitle}
        </h3>
      </div>
    </>
  );

  return (
    <motion.div
      id={plan.id}
      initial={{ opacity: 0, y: 28 }}
      animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 28 }}
      transition={{ duration: 0.55, delay: 0.05 + index * 0.08, ease: [0.25, 0.46, 0.45, 0.94] }}
      className="group flex h-full scroll-mt-24 flex-col overflow-hidden rounded-2xl border border-border bg-card transition-all duration-300 hover:-translate-y-1 hover:border-primary/40 hover:shadow-[0_16px_44px_-18px_rgba(107,173,165,0.35)]"
    >
      {/* PRODUCT COVER — крупно «вот именно это вы покупаете» */}
      {course?.slug ? (
        <Link
          href={routes.courseOverview(course.slug)}
          className={coverClass}
          aria-label={`Открыть «${productTitle}»`}
        >
          {coverInner}
        </Link>
      ) : (
        <div className={coverClass}>{coverInner}</div>
      )}

      {/* BODY */}
      <div className="flex flex-1 flex-col p-5 sm:p-6">
        {plan.description ? (
          <p className="line-clamp-2 text-sm leading-relaxed text-muted-foreground">
            {plan.description}
          </p>
        ) : null}

        <div className="mt-4">
          {personal.strikeLabel ? (
            <div className="mb-1 flex items-center gap-2">
              <s className="text-sm text-muted-foreground">{personal.strikeLabel}</s>
            </div>
          ) : plan.originalPriceLabel && plan.discountPercent ? (
            <div className="mb-1 flex items-center gap-2">
              <s className="text-sm text-muted-foreground">{plan.originalPriceLabel}</s>
              <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-[11px] font-semibold text-rose-600 dark:text-rose-400">
                −{plan.discountPercent}%
              </span>
            </div>
          ) : null}
          <span className="text-2xl font-bold tracking-tight sm:text-3xl">
            {personal.priceLabel}
          </span>
          <p className="mt-1 text-xs text-muted-foreground">
            {plan.discountEndsHint ? (
              <span className="font-medium text-rose-600 dark:text-rose-400">
                Акция {plan.discountEndsHint}
              </span>
            ) : null}
            {plan.discountEndsHint ? " · " : ""}
            {plan.priceNote}
          </p>
        </div>

        {plan.features.length > 0 && (
          <ul className="mt-4 space-y-2">
            {plan.features.slice(0, 3).map((feature) => (
              <li key={feature.text} className="flex items-start gap-2 text-sm text-foreground/75">
                <Icons.check className="mt-0.5 size-3.5 shrink-0 text-primary/70" />
                <span className="leading-relaxed">{feature.text}</span>
              </li>
            ))}
          </ul>
        )}

        <div className="mt-auto pt-5">
          <PlanPaymentBlock plan={plan} isOwned={isOwned} primaryCta />
        </div>
      </div>
    </motion.div>
  );
}

/**
 * Один курс bundle'а: title + slug + kind. Источник — `plan.includedCourses`
 * (backend, есть slug/kind), с fallback'ом на каталог по `courseIds`.
 */
interface BundleCourse {
  id: string;
  title: string;
  slug: string;
  kind: CourseKind;
}

function resolveBundleCourses(
  plan: PlanCard,
  courseById: Map<string, CourseCatalogDto>,
): BundleCourse[] {
  // Предпочитаем backend-список (есть slug + kind). Fallback — каталог по courseIds.
  if (plan.includedCourses && plan.includedCourses.length > 0) {
    return plan.includedCourses.map((c) => ({
      id: c.id,
      title: c.title,
      slug: c.slug,
      kind: c.kind,
    }));
  }
  return (plan.courseIds ?? [])
    .map((id) => courseById.get(id))
    .filter((c): c is CourseCatalogDto => Boolean(c))
    .map((c) => ({ id: c.id, title: c.title, slug: c.slug, kind: c.kind }));
}

/**
 * BundlePlanCard — крупная карточка COURSE-плана, покрывающего несколько курсов
 * (#404). Слева — оффер (бейдж, название, описание плана = доступ+возможности,
 * цена, CTA оплаты + upgrade-credit). Справа — вертикальный список ВСЕХ входящих
 * курсов: каждая строка кликабельна (ведёт на страницу курса) + kind-бейдж.
 *
 * Описание плана (`plan.description` = shortDescription) описывает доступ и
 * возможности — НЕ дублирует материалы курсов; за описанием каждого курса юзер
 * идёт по чипу на его страницу.
 *
 * Никаких nested-anchor'ов: список курсов и платёжная кнопка — siblings, вся
 * карточка в `<a>` не оборачивается.
 */
function BundlePlanCard({
  plan,
  index,
  inView,
  isOwned,
  courseById,
  className,
}: {
  plan: PlanCard;
  index: number;
  inView: boolean;
  isOwned: boolean;
  courseById: Map<string, CourseCatalogDto>;
  className?: string;
}) {
  const courses = resolveBundleCourses(plan, courseById);
  const personal = usePersonalPrice(plan, isOwned);

  return (
    <motion.div
      id={plan.id}
      initial={{ opacity: 0, y: 28 }}
      animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 28 }}
      transition={{ duration: 0.6, delay: 0.05 + index * 0.08, ease: [0.25, 0.46, 0.45, 0.94] }}
      className={cn("scroll-mt-24", className)}
    >
      <div className="relative overflow-hidden rounded-2xl border border-primary/30 bg-gradient-to-br from-primary/[0.05] via-card to-card shadow-[0_0_50px_-20px_rgba(107,173,165,0.3)] transition-all duration-300 hover:-translate-y-1 hover:border-primary/45 hover:shadow-[0_18px_50px_-18px_rgba(107,173,165,0.4)]">
        <div
          aria-hidden
          className="pointer-events-none absolute -right-20 -top-20 size-64 rounded-full bg-primary/10 blur-3xl"
        />
        <div className="relative grid gap-7 p-6 sm:p-8 lg:grid-cols-[0.95fr_1.05fr] lg:gap-9 lg:p-10">
          {/* LEFT — оффер + цена + CTA */}
          <div className="flex flex-col">
            <span className="inline-flex w-fit items-center gap-1.5 rounded-full border border-primary/30 bg-primary/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-primary">
              <Icons.layers className="size-3.5" />
              Набор из {courses.length} курсов
            </span>
            <h3 className="mt-4 text-2xl font-bold tracking-tight sm:text-3xl">{plan.name}</h3>
            {plan.description ? (
              <p className="mt-3 max-w-md text-sm leading-relaxed text-muted-foreground sm:text-[15px]">
                {plan.description}
              </p>
            ) : null}

            <div className="mt-6">
              {personal.strikeLabel ? (
                <div className="mb-1.5 flex items-center gap-2">
                  <s className="text-base text-muted-foreground">{personal.strikeLabel}</s>
                </div>
              ) : plan.originalPriceLabel && plan.discountPercent ? (
                <div className="mb-1.5 flex items-center gap-2">
                  <s className="text-base text-muted-foreground">{plan.originalPriceLabel}</s>
                  <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-xs font-semibold text-rose-600 dark:text-rose-400">
                    −{plan.discountPercent}%
                  </span>
                </div>
              ) : null}
              <div className="text-3xl font-bold tracking-tight sm:text-4xl xl:text-5xl">
                {personal.priceLabel}
              </div>
              <p className="mt-2 text-xs text-muted-foreground sm:text-sm">
                {plan.discountEndsHint ? (
                  <span className="font-medium text-rose-600 dark:text-rose-400">
                    Акция {plan.discountEndsHint}
                  </span>
                ) : null}
                {plan.discountEndsHint ? " · " : ""}
                {plan.priceNote}
              </p>
            </div>

            {plan.features.length > 0 && (
              <ul className="mt-6 space-y-2.5">
                {plan.features.slice(0, 5).map((feature) => (
                  <li
                    key={feature.text}
                    className="flex items-start gap-2.5 text-sm text-foreground/80"
                  >
                    <Icons.check className="mt-0.5 size-4 shrink-0 text-primary" />
                    <span className="leading-relaxed">{feature.text}</span>
                  </li>
                ))}
              </ul>
            )}

            <div className="mt-auto pt-7">
              <PlanPaymentBlock plan={plan} isOwned={isOwned} />
            </div>
          </div>

          {/* RIGHT — список входящих курсов (кликабельные чипы) */}
          <div className="rounded-2xl border border-border/60 bg-background/40 p-5 backdrop-blur-sm sm:p-6">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Что входит
              <span className="ml-1.5 font-normal normal-case tracking-normal text-muted-foreground/80">
                {courses.length} курсов в одном плане
              </span>
            </p>
            {courses.length > 0 ? (
              <ul className="mt-4 space-y-2">
                {courses.map((course) => (
                  <li key={course.id}>
                    <BundleCourseRow course={course} />
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-4 text-sm text-muted-foreground">Список курсов появится здесь.</p>
            )}
          </div>
        </div>
      </div>
    </motion.div>
  );
}

/**
 * Кликабельная строка курса в bundle-карточке — sibling-ссылка (не вложена в
 * карточку-`<a>`). Название + kind-бейдж + chevron «открыть курс».
 */
function BundleCourseRow({ course }: { course: BundleCourse }) {
  const kindBadge = getCourseKindBadge(course.kind);
  return (
    <Link
      href={routes.courseOverview(course.slug)}
      className="group flex items-center gap-3 rounded-xl border border-border/50 bg-card/60 px-3.5 py-3 transition-all duration-200 hover:-translate-y-0.5 hover:border-primary/40 hover:bg-card hover:shadow-sm"
    >
      <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
        <KindGlyph kind={course.kind} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-semibold text-foreground transition-colors group-hover:text-primary">
          {course.title}
        </span>
        <span
          className={cn(
            "mt-0.5 inline-flex items-center rounded px-1.5 py-0.5 text-[10px] font-semibold",
            kindBadge.class,
          )}
        >
          {COURSE_KIND_LABELS[course.kind]}
        </span>
      </span>
      <Icons.chevronRight className="size-4 shrink-0 text-muted-foreground/40 transition-all duration-200 group-hover:translate-x-0.5 group-hover:text-primary" />
    </Link>
  );
}

/** Глиф типа курса для строки bundle'а (теми же иконками, что и KindGradient). */
function KindGlyph({ kind }: { kind: CourseKind }) {
  const Glyph =
    { COURSE: Icons.graduation, INTENSIVE: Icons.energy, MARATHON: Icons.target }[kind] ??
    Icons.graduation;
  return <Glyph className="size-4" />;
}

/**
 * GenericPlanCard — стандартная вертикальная карточка плана. Используется для
 * подписок, вторичных full-планов (compact) и static-планов лендинга.
 * COURSE-планы рендерятся отдельным ProductPlanCard (обложка крупно).
 */
function GenericPlanCard({
  plan,
  index,
  inView,
  isOwned,
  compact = false,
}: {
  plan: PlanCard;
  index: number;
  inView: boolean;
  isOwned: boolean;
  compact?: boolean;
}) {
  const personal = usePersonalPrice(plan, isOwned);
  // Подписочный лейбл «/ мес» рядом с ценой (#614) — только когда план recurring и
  // не куплен (купленный показывает статус «У вас уже есть», цена скрыта в payment-блоке).
  const cadence = isOwned ? null : cadenceLabel(plan.termRecurringDays);
  return (
    <motion.div
      id={plan.id}
      initial={{ opacity: 0, y: 32 }}
      animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 32 }}
      transition={{ duration: 0.6, delay: 0.1 + index * 0.1, ease: [0.25, 0.46, 0.45, 0.94] }}
    >
      <Card
        className={cn(
          "relative h-full gap-0 overflow-hidden transition-all duration-300 hover:-translate-y-1",
          compact ? "p-5 sm:p-6" : "p-6 sm:p-8",
          plan.highlighted &&
            "border-primary/40 bg-primary/[0.02] shadow-[0_0_40px_-12px_rgba(107,173,165,0.25)]",
        )}
      >
        {plan.highlighted && !compact && (
          <span className="absolute right-6 top-6 rounded-full bg-primary px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-primary-foreground">
            Хит
          </span>
        )}

        <span
          className={cn(
            "inline-flex w-fit items-center rounded-full border px-3 py-1 text-[11px] font-semibold uppercase tracking-wider",
            plan.highlighted
              ? "border-primary/30 bg-primary/10 text-primary"
              : "border-border bg-muted/50 text-muted-foreground",
          )}
        >
          {plan.badge}
        </span>

        <h3
          className={cn(
            "mt-5 font-bold tracking-tight",
            compact ? "text-xl" : "text-2xl xl:text-[1.75rem]",
          )}
        >
          {plan.name}
        </h3>
        <p className="mt-2 text-sm leading-relaxed text-muted-foreground xl:text-[15px]">
          {plan.description}
        </p>

        <div className="mt-6 border-y border-border py-5">
          {personal.strikeLabel ? (
            <div className="mb-1.5 flex items-center gap-2">
              <s className="text-base text-muted-foreground">{personal.strikeLabel}</s>
            </div>
          ) : plan.originalPriceLabel && plan.discountPercent ? (
            <div className="mb-1.5 flex items-center gap-2">
              <s className="text-base text-muted-foreground">{plan.originalPriceLabel}</s>
              <span className="rounded-full bg-rose-500/15 px-2 py-0.5 text-xs font-semibold text-rose-600 dark:text-rose-400">
                −{plan.discountPercent}%
              </span>
            </div>
          ) : null}
          <div
            className={cn(
              "flex items-baseline gap-1.5 font-bold tracking-tight",
              compact
                ? "text-3xl text-foreground"
                : plan.highlighted
                  ? "text-4xl text-primary xl:text-5xl"
                  : "text-4xl text-foreground",
            )}
          >
            <span>{personal.priceLabel}</span>
            {cadence ? (
              <span className="text-base font-medium text-muted-foreground">{cadence}</span>
            ) : null}
          </div>
          <p className="mt-2 text-xs text-muted-foreground sm:text-sm">
            {plan.discountEndsHint ? (
              <span className="font-medium text-rose-600 dark:text-rose-400">
                Акция {plan.discountEndsHint}
              </span>
            ) : null}
            {plan.discountEndsHint ? " · " : ""}
            {plan.priceNote}
          </p>
        </div>

        <ul className="mt-6 flex-1 space-y-3">
          {plan.features.map((feature) => (
            <li key={feature.text} className="flex items-start gap-3">
              <Icons.check
                className={cn(
                  "mt-0.5 size-4 shrink-0",
                  feature.highlight || plan.highlighted ? "text-primary" : "text-primary/60",
                )}
              />
              <span
                className={cn(
                  "text-sm leading-relaxed xl:text-[15px]",
                  feature.highlight ? "font-medium text-foreground" : "text-foreground/75",
                )}
              >
                {feature.text}
              </span>
            </li>
          ))}
        </ul>

        {!compact && <PlanIncludedContent plan={plan} />}

        <PlanPaymentBlock plan={plan} isOwned={isOwned} />
      </Card>
    </motion.div>
  );
}

/**
 * #521: личная цена для ГЛАВНОГО ценника карточки. Тот же quote и enabled-условия,
 * что и в PlanPaymentBlock — React Query дедупит запрос по queryKey, сети +0.
 */
function usePersonalPrice(plan: PlanCard, isOwned: boolean) {
  const isAuthenticated = useIsAuthenticated();
  const cardKind: PlanCardKind = plan.kind ?? (plan.priceAmount === 0 ? "free" : "paid");
  const { data: quote } = useQuery({
    ...upgradeQuoteQueryOptions(plan.planId),
    enabled: isAuthenticated && Boolean(plan.planId) && cardKind === "paid" && !isOwned,
  });
  return personalPriceParts(
    { priceLabel: plan.price, currency: plan.currency ?? "RUB" },
    isOwned ? null : quote,
  );
}

// ---------------------------------------------------------------------------
// PAYMENT BLOCK — CTA «Оплатить X ₽» + рассрочка + payment methods
// ---------------------------------------------------------------------------

function PlanPaymentBlock({
  plan,
  isOwned,
  primaryCta = false,
  ctaLabel,
}: {
  plan: PlanCard;
  isOwned: boolean;
  primaryCta?: boolean;
  /** Переопределяет текст CTA-кнопки (напр. «Попробовать месяц» для trial, #595). */
  ctaLabel?: string;
}) {
  // kind===undefined → backward-compat для STATIC_PLANS: priceAmount===0 значит «free».
  const cardKind: PlanCardKind = plan.kind ?? (plan.priceAmount === 0 ? "free" : "paid");
  const isAuthenticated = useIsAuthenticated();
  const billingEnabled = useBillingEnabled();

  // Phase 2 #112: для платных планов с реальным backend planId — fetch'им upgrade-quote.
  // Показываем индивидуальную цену (final) + breakdown «–N ₽ за уже куплен X».
  // STATIC_PLANS (planId == null) — fallback на plan.price без quote'а.
  const { data: quote } = useQuery({
    ...upgradeQuoteQueryOptions(plan.planId),
    enabled: isAuthenticated && Boolean(plan.planId) && cardKind === "paid" && !isOwned,
  });

  const hasCredit = Boolean(quote && quote.creditCents > 0);
  const showPaymentMethods = cardKind === "paid" && !isOwned;
  const showInstallment =
    cardKind === "paid" && !isOwned && Boolean(plan.installmentLabel) && !hasCredit;

  if (isOwned) {
    return (
      <div className="mt-8 space-y-3">
        <div
          className={cn(
            "inline-flex w-full items-center justify-center gap-2 rounded-xl border-2 border-emerald-500/40 bg-emerald-500/5 px-6 py-3.5 text-sm font-semibold text-emerald-600 dark:text-emerald-400",
          )}
        >
          <Icons.check className="size-4" />У вас уже есть этот план
        </div>
        <p className="text-center text-[11px] text-muted-foreground/70">
          Доступ открыт — повторная покупка не нужна.
        </p>
      </div>
    );
  }

  return (
    <div className="mt-8 space-y-3">
      {hasCredit && quote && <UpgradeCreditBreakdown quote={quote} originalPrice={plan.price} />}

      <PaymentButton
        plan={plan}
        cardKind={cardKind}
        quote={quote ?? null}
        primaryCta={primaryCta}
        ctaLabel={ctaLabel}
      />

      {showInstallment && (
        <div className="flex items-center justify-center gap-2 rounded-lg border border-border/60 bg-muted/30 px-3 py-2 text-xs text-muted-foreground sm:text-sm">
          <Icons.calendar className="size-3.5" />
          <span>Возможна оплата долями или в рассрочку</span>
        </div>
      )}

      {showPaymentMethods && (
        <div className="flex flex-wrap items-center justify-center gap-1.5 pt-2">
          {PAYMENT_METHODS.map((method) => (
            <span
              key={method}
              className="rounded-md border border-border/60 bg-muted/30 px-2 py-1 text-[10px] font-medium uppercase tracking-wider text-muted-foreground"
            >
              {method}
            </span>
          ))}
        </div>
      )}

      <p className="text-center text-[11px] text-muted-foreground/70">
        {cardKind === "free"
          ? "Доступно после регистрации"
          : cardKind === "tbd"
            ? "Цена уточняется — напишите автору в Telegram"
            : billingEnabled && plan.planId && plan.priceCents
              ? "Безопасная оплата через T-Bank · 3-D Secure"
              : "Пока оформление через Telegram — в ближайшее время подключим прямую оплату"}
      </p>

      {cardKind === "paid" && !isOwned && (
        <Link
          href={PRICING_CTA_HREF}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border/60 bg-muted/20 px-3 py-2 text-xs font-medium text-muted-foreground transition-colors hover:border-primary/40 hover:bg-muted/40 hover:text-foreground sm:text-sm"
        >
          <TelegramGlyph />
          Есть вопрос по плану? Напишите автору
        </Link>
      )}
    </div>
  );
}

/**
 * Кнопка оплаты. Две ветки:
 * - **paid + backend planId + priceCents** → `BuyPlanButton` (T-Bank checkout).
 * - **legacy fallback** (STATIC_PLANS, tbd, или backend без цены) → ссылка
 *   на Telegram автора, как было до payment integration.
 *
 * Issue #358: free-plan claim flow удалён — бесплатный доступ теперь = system default,
 * без отдельного плана и CTA.
 *
 * Phase 2 #112 / #486: при наличии quote с credit'ом финальная цена идёт и в
 * legacy CTA-label, и в `BuyPlanButton` (T-Bank) — кнопка обязана показывать ту же
 * сумму, которую `CreateOrderHandler` посчитает через UpgradeCreditCalculator.
 */
function PaymentButton({
  plan,
  cardKind,
  quote,
  primaryCta = false,
  ctaLabel: ctaLabelOverride,
}: {
  plan: PlanCard;
  cardKind: PlanCardKind;
  quote: UpgradeQuoteDto | null;
  primaryCta?: boolean;
  /** Переопределяет текст CTA (напр. «Попробовать месяц», #595). */
  ctaLabel?: string;
}) {
  const router = useRouter();
  const isAuthenticated = useIsAuthenticated();
  const billingEnabled = useBillingEnabled();

  // `primaryCta` — для одиночных product-карточек (один курс/интенсив): «Оплатить»
  // всегда основная/залитая кнопка. `highlighted` различает планы только при
  // сравнении нескольких full-планов рядом (GenericPlanCard).
  const buttonClass = cn(
    "inline-flex w-full items-center justify-center gap-2 rounded-xl px-6 py-3.5 text-sm font-semibold transition-all duration-300",
    plan.highlighted || primaryCta
      ? "bg-primary text-primary-foreground shadow-[0_0_24px_-8px_rgba(107,173,165,0.5)] hover:bg-primary/90 hover:shadow-[0_0_32px_-6px_rgba(107,173,165,0.65)]"
      : "border border-border bg-muted/40 text-foreground hover:bg-muted hover:border-foreground/20",
  );

  // Gate'им T-Bank checkout за feature flag — на проде он пока выключен,
  // fallback на Telegram-консультацию автора (legacy ниже).
  if (billingEnabled && cardKind === "paid" && plan.planId && plan.priceCents) {
    const payableCents =
      quote && quote.creditCents > 0 && quote.finalPriceCents != null
        ? quote.finalPriceCents
        : plan.priceCents;
    return (
      <BuyPlanButton
        planId={plan.planId}
        planSlug={plan.id}
        priceCents={payableCents}
        currency={plan.currency ?? "RUB"}
        className={buttonClass}
        label={ctaLabelOverride}
      />
    );
  }

  const icon =
    cardKind === "free" ? null : cardKind === "tbd" ? (
      <Icons.help className="size-4" />
    ) : (
      <Icons.creditCard className="size-4" />
    );

  const ctaLabel =
    ctaLabelOverride ??
    (quote && quote.creditCents > 0 && quote.finalPriceCents != null
      ? `Оплатить ${Math.floor(quote.finalPriceCents / 100).toLocaleString("ru-RU")} ₽`
      : plan.paymentCta);

  // Static «free» CTA (issue #358): anonymous → login, authenticated → /home.
  // Бесплатный «план» — больше не grant flow, а promo-card на лендинге.
  if (cardKind === "free") {
    if (isAuthenticated) {
      return (
        <Link href="/home" className={buttonClass}>
          {icon}
          Открыть кабинет
        </Link>
      );
    }
    const callback = `/pricing#${plan.id}`;
    return (
      <Link href={`/login?callbackUrl=${encodeURIComponent(callback)}`} className={buttonClass}>
        {icon}
        {ctaLabel}
      </Link>
    );
  }

  const handleClick = (e: React.MouseEvent) => {
    if (!isAuthenticated) {
      e.preventDefault();
      const callback = `/pricing#${plan.id}`;
      router.push(`/login?callbackUrl=${encodeURIComponent(callback)}`);
    }
  };

  if (isAuthenticated) {
    return (
      <Link
        href={PRICING_CTA_HREF}
        target="_blank"
        rel="noopener noreferrer"
        className={buttonClass}
      >
        {icon}
        {ctaLabel}
      </Link>
    );
  }

  return (
    <button type="button" onClick={handleClick} className={buttonClass}>
      {icon}
      {ctaLabel}
    </button>
  );
}

// ---------------------------------------------------------------------------
// COMPARISON TABLE
// ---------------------------------------------------------------------------

function PricingComparison({ plans }: { plans: PlanCard[] }) {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, margin: "-10%" });

  return (
    <section className="relative py-16 md:py-24">
      <div ref={ref} className="mx-auto max-w-6xl px-6">
        <motion.div
          className="text-center"
          initial={{ opacity: 0, y: 20 }}
          animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 20 }}
          transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          <p className="text-xs font-medium uppercase tracking-[0.2em] text-primary/80">
            Сравнение
          </p>
          <h2 className="mt-3 text-2xl font-bold tracking-tight sm:text-3xl md:text-4xl">
            Что входит в каждый план
          </h2>
        </motion.div>

        {/* Desktop: matrix table */}
        <div className="mt-10 hidden overflow-hidden rounded-2xl border border-border bg-card lg:block">
          <table className="w-full">
            <thead>
              <tr className="border-b border-border">
                <th className="w-2/5 px-6 py-5 text-left text-sm font-semibold text-muted-foreground">
                  Фича
                </th>
                {plans.map((plan) => (
                  <th
                    key={plan.id}
                    className={cn(
                      "px-4 py-5 text-center text-sm font-semibold",
                      plan.highlighted ? "text-primary" : "text-foreground",
                    )}
                  >
                    {plan.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {COMPARISON_GROUPS.map((group) => (
                <ComparisonGroupRows key={group.title} group={group} plans={plans} />
              ))}
            </tbody>
          </table>
        </div>

        {/* Mobile: per-plan stacked sections */}
        <div className="mt-10 space-y-4 lg:hidden">
          {plans.map((plan) => (
            <MobileComparisonCard key={plan.id} plan={plan} />
          ))}
        </div>
      </div>
    </section>
  );
}

function ComparisonGroupRows({
  group,
  plans,
}: {
  group: (typeof COMPARISON_GROUPS)[number];
  plans: PlanCard[];
}) {
  return (
    <>
      <tr className="bg-muted/30">
        <td
          colSpan={plans.length + 1}
          className="px-6 py-3 text-xs font-semibold uppercase tracking-wider text-muted-foreground"
        >
          {group.title}
        </td>
      </tr>
      {group.rows.map((row) => (
        <tr key={row.key} className="border-t border-border/60">
          <td className="px-6 py-4 text-sm text-foreground/85">
            {row.label}
            {row.hint && <span className="ml-2 text-xs text-muted-foreground">— {row.hint}</span>}
          </td>
          {plans.map((plan) => {
            const has = plan.comparisonKeys.includes(row.key);
            return (
              <td key={plan.id} className="px-4 py-4 text-center">
                {has ? (
                  <Icons.check
                    className={cn(
                      "inline size-5",
                      plan.highlighted ? "text-primary" : "text-primary/70",
                    )}
                  />
                ) : (
                  <span className="text-muted-foreground/40">—</span>
                )}
              </td>
            );
          })}
        </tr>
      ))}
    </>
  );
}

function MobileComparisonCard({ plan }: { plan: PlanCard }) {
  return (
    <Card className={cn("gap-0 p-6", plan.highlighted && "border-primary/30 bg-primary/[0.02]")}>
      <h3
        className={cn("text-lg font-bold", plan.highlighted ? "text-primary" : "text-foreground")}
      >
        {plan.name}
      </h3>
      <div className="mt-5 space-y-5">
        {COMPARISON_GROUPS.map((group) => {
          const groupRows = group.rows.filter((r) => plan.comparisonKeys.includes(r.key));
          if (groupRows.length === 0) return null;
          return (
            <div key={group.title}>
              <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                {group.title}
              </p>
              <ul className="mt-2 space-y-2">
                {groupRows.map((row) => (
                  <li key={row.key} className="flex items-start gap-2.5 text-sm text-foreground/80">
                    <Icons.check className="mt-0.5 size-4 shrink-0 text-primary" />
                    {row.label}
                  </li>
                ))}
              </ul>
            </div>
          );
        })}
      </div>
    </Card>
  );
}

// ---------------------------------------------------------------------------
// FAQ
// ---------------------------------------------------------------------------

function PricingFaq() {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, margin: "-10%" });

  return (
    <section className="relative bg-muted/30 py-16 md:py-24">
      <div ref={ref} className="mx-auto max-w-3xl px-6">
        <motion.div
          className="text-center"
          initial={{ opacity: 0, y: 20 }}
          animate={inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 20 }}
          transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          <p className="text-xs font-medium uppercase tracking-[0.2em] text-primary/80">
            Частые вопросы
          </p>
          <h2 className="mt-3 text-2xl font-bold tracking-tight sm:text-3xl md:text-4xl">
            Что важно знать
          </h2>
        </motion.div>

        <Accordion
          type="single"
          collapsible
          className="mt-10 space-y-3"
          defaultValue={PRICING_FAQ[0]?.question}
        >
          {PRICING_FAQ.map((item) => (
            <AccordionItem
              key={item.question}
              value={item.question}
              className="overflow-hidden rounded-2xl border border-border bg-card px-6 transition-colors hover:border-foreground/15"
            >
              <AccordionTrigger className="py-5 text-left text-sm font-medium text-foreground hover:no-underline sm:text-base">
                <span className="flex-1">{item.question}</span>
              </AccordionTrigger>
              <AccordionContent className="text-sm leading-relaxed text-muted-foreground sm:text-[15px]">
                <p>{item.answer}</p>
                {item.telegramCta ? (
                  <div className="mt-3">
                    <a
                      href={PRICING_CTA_HREF}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 font-medium text-primary hover:underline"
                    >
                      <TelegramGlyph />
                      Написать в Telegram
                    </a>
                  </div>
                ) : null}
              </AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------
// TRUST + FINAL CTA
// ---------------------------------------------------------------------------

function PricingTrustAndCta() {
  return (
    <section className="relative py-16 md:py-24">
      <div className="mx-auto max-w-4xl px-6 text-center">
        <h2 className="text-2xl font-bold tracking-tight sm:text-3xl md:text-4xl">
          Остались вопросы?
        </h2>
        <p className="mx-auto mt-4 max-w-xl text-base text-muted-foreground md:text-lg">
          Напиши лично — расскажу, с чего начать под твой уровень. Помогу с рассрочкой и оплатой из
          любой страны.
        </p>

        <Link
          href={PRICING_CTA_HREF}
          target="_blank"
          rel="noopener noreferrer"
          className="mt-8 inline-flex items-center gap-2 rounded-xl bg-primary px-7 py-3.5 text-sm font-semibold text-primary-foreground transition-all hover:bg-primary/90 hover:shadow-[0_0_24px_-8px_rgba(107,173,165,0.5)]"
        >
          <TelegramGlyph />
          Написать в Telegram
        </Link>

        <div className="mx-auto mt-10 flex max-w-3xl flex-wrap items-center justify-center gap-x-8 gap-y-3 text-xs text-muted-foreground sm:text-sm">
          <TrustItem icon={<Icons.shield className="size-4" />} label="3 дня — полный возврат" />
          <TrustItem
            icon={<Icons.calendar className="size-4" />}
            label="Рассрочка от 7 000 ₽/мес"
          />
          <TrustItem
            icon={<Icons.globe className="size-4" />}
            label="Оплата из РФ и из-за рубежа"
          />
        </div>
      </div>
    </section>
  );
}

function TrustItem({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-primary/60">{icon}</span>
      <span>{label}</span>
    </div>
  );
}

function TelegramGlyph() {
  return (
    <svg className="size-4" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z" />
    </svg>
  );
}
