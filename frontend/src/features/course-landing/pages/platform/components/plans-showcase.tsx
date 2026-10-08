"use client";

import { useRef } from "react";
import Link from "next/link";
import { motion, useInView } from "framer-motion";
import { useQuery } from "@tanstack/react-query";
import { Icons } from "@/shared/ui/icons";
import {
  formatPriceFromCents,
  formatPromotionEndsHint,
  isTrialPlan,
  publicPlansQueryOptions,
  type PublicPlanDto,
} from "@/entities/access-plan";
import { PLANS, type PlanCard } from "@/shared/config/landing-plans";
import { routes } from "@/shared/config/routes";
import { useReducedMotion } from "../hooks/use-reduced-motion";

// ---------------------------------------------------------------------------
// PlansShowcase — pricing-блок лендинга: единственный тариф «Полный доступ»
// (#1160, offer-v2). Детали и оплата — на /pricing (CTA ниже).
// ---------------------------------------------------------------------------

const FULL_ACCESS_PLAN = PLANS.find((p) => p.id === "lifetime");

/**
 * Оверлеит живую цену + акцию (backend `PublicPlanDto`) поверх захардкоженной
 * флагман-карточки. Hardcoded остаётся fallback'ом, пока данные не загрузились
 * (или access-сервис недоступен) — лендинг быстрый и не зависит от его доступности.
 */
function withLivePricing(base: PlanCard, dto: PublicPlanDto | undefined): PlanCard {
  if (!dto || dto.priceCents == null || dto.priceCents <= 0) return base;
  const promo = dto.promotionActive && dto.effectivePriceCents != null;
  const effectiveCents = promo ? dto.effectivePriceCents! : dto.priceCents;
  return {
    ...base,
    price: formatPriceFromCents(effectiveCents, dto.currency),
    priceCents: effectiveCents,
    currency: dto.currency,
    originalPriceLabel: promo ? formatPriceFromCents(dto.priceCents, dto.currency) : null,
    discountPercent: promo ? dto.discountPercent : null,
    discountEndsHint: promo ? formatPromotionEndsHint(dto.discountEndsAt) : null,
  };
}

export function PlansShowcase() {
  const ref = useRef<HTMLDivElement>(null);
  const inView = useInView(ref, { once: true, margin: "-15%" });
  const reduced = useReducedMotion();

  // Живые цены флагмана (FULL_ALL) с акцией — клиентский остров в статичном
  // лендинге. До загрузки (или если access-сервис недоступен) рендерим
  // hardcoded fallback. Public endpoint отдаёт опубликованные планы платформы (#608).
  const { data: plans } = useQuery(publicPlansQueryOptions());
  // Бессрочный FULL_ALL — флагман. Trial-план («Полный доступ на месяц») детектим
  // по trialDurationDays, чтобы не подменить им флагман. (#595)
  const liveFullAccess = plans?.find((p) => p.tier === "FULL_ALL" && !isTrialPlan(p));
  const hasTrialPlan = (plans ?? []).some(isTrialPlan);
  const flagship = FULL_ACCESS_PLAN ? withLivePricing(FULL_ACCESS_PLAN, liveFullAccess) : undefined;
  const teasers: PlanCard[] = flagship ? [flagship] : [];

  return (
    <section
      id="price"
      className="relative overflow-hidden py-20 md:py-28 lg:py-32"
      style={{
        background:
          "radial-gradient(ellipse 900px 600px at 50% 30%, rgba(201,168,76,0.04), transparent 60%), #0A0A0B",
      }}
    >
      <div ref={ref} className="relative mx-auto max-w-7xl px-6">
        {/* Header */}
        <motion.div
          className="text-center"
          initial={reduced ? false : { opacity: 0, y: 20 }}
          animate={reduced || inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 20 }}
          transition={{ duration: 0.6, ease: [0.25, 0.46, 0.45, 0.94] }}
        >
          <p className="text-xs font-medium uppercase tracking-[0.2em] text-[#6BADA5]/70">Доступ</p>
          <h2 className="mt-3 text-3xl font-bold tracking-tight sm:text-4xl md:text-5xl">
            Один доступ — обучение .NET Fullstack
          </h2>
          <p className="mx-auto mt-4 max-w-xl text-sm text-white/50 sm:text-base">
            Вся программа .NET Fullstack, оба уровня, AI-ревью PR, закрытый чат и поддержка автора.
            Оплата один раз — доступ навсегда.
          </p>
          <div className="mx-auto mt-6 flex max-w-xl flex-wrap items-center justify-center gap-2 text-xs sm:text-sm">
            {["Оба уровня программы", "AI-ревью PR", "Закрытый чат", "Без подписки"].map((t) => (
              <span
                key={t}
                className="rounded-full border border-white/10 bg-white/[0.03] px-3 py-1 text-white/55"
              >
                {t}
              </span>
            ))}
          </div>
        </motion.div>

        {/* Единственный тариф — «Полный доступ». Детали и оплата — на /pricing. */}
        <div className="mx-auto mt-16 grid max-w-md gap-6 md:gap-8 lg:mt-20">
          {teasers.map((plan, i) => (
            <PlanCardView key={plan.id} plan={plan} index={i} inView={inView} reduced={reduced} />
          ))}
        </div>

        {/* Подчинённая строка-пилюля «не готов навсегда?» (#595) — ведёт на
            /pricing, где в hero «Полного доступа» можно переключиться на месяц.
            Показываем только если на платформе есть опубликованный trial-план. */}
        {hasTrialPlan ? (
          <div className="mt-8 flex justify-center md:mt-10">
            <Link
              href={routes.pricing}
              data-growth-cta="pricing_trial"
              data-growth-placement="pricing"
              className="group inline-flex items-center gap-2 rounded-full border border-white/10 bg-white/[0.03] px-4 py-2 text-xs text-white/55 transition-all hover:border-[#6BADA5]/40 hover:bg-white/[0.06] hover:text-white/80 sm:text-sm"
            >
              <Icons.clock className="size-3.5 shrink-0 text-[#6BADA5]" />
              Не готов навсегда? Начни с месяца — деньги в зачёт
              <Icons.arrowRight className="size-3.5 transition-transform group-hover:translate-x-0.5" />
            </Link>
          </div>
        ) : null}

        {/* Trust row */}
        <motion.div
          className="mx-auto mt-12 flex max-w-3xl flex-wrap items-center justify-center gap-x-8 gap-y-3 text-xs text-white/35 sm:text-sm md:mt-16"
          initial={reduced ? false : { opacity: 0 }}
          animate={reduced || inView ? { opacity: 1 } : { opacity: 0 }}
          transition={{ duration: 0.6, delay: 0.5, ease: "easeOut" }}
        >
          <TrustItem icon={<Icons.shield className="size-4" />} label="3 дня — полный возврат" />
          <TrustItem
            icon={<Icons.calendar className="size-4" />}
            label="Рассрочка от 7 000 ₽/мес"
          />
          <TrustItem
            icon={<Icons.globe className="size-4" />}
            label="Оплата из РФ и из-за рубежа"
          />
        </motion.div>

        {/* All plans CTA — полный каталог планов на странице «Доступ» */}
        <div className="mt-10 text-center">
          <Link
            href={routes.pricing}
            data-growth-cta="pricing_all"
            data-growth-placement="pricing"
            className="group inline-flex items-center gap-2 rounded-xl border border-white/12 bg-white/[0.04] px-6 py-3 text-sm font-semibold text-white/85 transition-all hover:border-[#6BADA5]/40 hover:bg-white/[0.07]"
          >
            Подробнее о тарифе
            <Icons.arrowRight className="size-4 transition-transform group-hover:translate-x-0.5" />
          </Link>
        </div>
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------
// PlanCardView — высокая карточка с stagger features animation
// ---------------------------------------------------------------------------

function PlanCardView({
  plan,
  index,
  inView,
  reduced,
}: {
  plan: PlanCard;
  index: number;
  inView: boolean;
  reduced: boolean;
}) {
  const accentColor = plan.accent === "gold" ? "#C9A84C" : "#6BADA5";
  const isGold = plan.accent === "gold";

  return (
    <motion.article
      className={`group relative flex flex-col rounded-3xl border bg-[#0F0F11] p-7 transition-all duration-500 hover:-translate-y-1 sm:p-8 xl:p-10 ${
        plan.highlighted ? "xl:scale-[1.04] xl:py-12" : ""
      }`}
      style={{
        borderColor: plan.highlighted ? `${accentColor}40` : "rgba(255,255,255,0.06)",
        boxShadow: plan.highlighted
          ? `0 0 60px ${accentColor}12, inset 0 1px 0 ${accentColor}1a`
          : "none",
      }}
      initial={reduced ? false : { opacity: 0, y: 40 }}
      animate={reduced || inView ? { opacity: 1, y: 0 } : { opacity: 0, y: 40 }}
      transition={{
        duration: 0.7,
        delay: 0.15 + index * 0.12,
        ease: [0.25, 0.46, 0.45, 0.94],
      }}
    >
      {/* Subtle gold gradient overlay for highlighted */}
      {plan.highlighted && (
        <>
          <div
            className="pointer-events-none absolute inset-0 rounded-3xl opacity-40"
            style={{
              background: `radial-gradient(ellipse at top, ${accentColor}10, transparent 70%)`,
            }}
          />
          {!reduced && (
            <motion.div
              className="pointer-events-none absolute -inset-px rounded-3xl"
              style={{
                background: `linear-gradient(135deg, ${accentColor}30, transparent 50%, ${accentColor}30)`,
                opacity: 0,
                filter: "blur(8px)",
              }}
              animate={{ opacity: [0.3, 0.6, 0.3] }}
              transition={{ duration: 4, repeat: Infinity, ease: "easeInOut" }}
            />
          )}
        </>
      )}

      <div className="relative flex flex-1 flex-col">
        {/* Badge */}
        <div className="flex items-center gap-2">
          <span
            className="inline-flex items-center rounded-full px-3 py-1 text-[11px] font-semibold uppercase tracking-wider"
            style={
              isGold
                ? {
                    color: accentColor,
                    backgroundColor: `${accentColor}1a`,
                    border: `1px solid ${accentColor}40`,
                  }
                : plan.accent === "neutral"
                  ? {
                      color: "rgba(255,255,255,0.55)",
                      backgroundColor: "rgba(255,255,255,0.04)",
                      border: "1px solid rgba(255,255,255,0.08)",
                    }
                  : {
                      color: accentColor,
                      backgroundColor: `${accentColor}10`,
                      border: `1px solid ${accentColor}30`,
                    }
            }
          >
            {plan.badge}
          </span>
        </div>

        {/* Name + description */}
        <h3
          className={`mt-5 font-bold tracking-tight ${
            plan.highlighted ? "text-3xl xl:text-[2rem]" : "text-2xl"
          }`}
        >
          {plan.name}
        </h3>
        <p className="mt-2 text-sm leading-relaxed text-white/45 lg:text-[15px]">
          {plan.description}
        </p>

        {/* Price */}
        <div className="mt-7 border-y border-white/[0.05] py-6">
          {plan.originalPriceLabel && (
            <div className="mb-2 flex items-center gap-2.5">
              <s className="text-base text-white/30">{plan.originalPriceLabel}</s>
              {plan.discountPercent != null && (
                <span
                  className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
                  style={{
                    color: accentColor,
                    backgroundColor: `${accentColor}1a`,
                    border: `1px solid ${accentColor}33`,
                  }}
                >
                  −{plan.discountPercent}%
                </span>
              )}
            </div>
          )}
          <motion.div
            className={`font-bold tracking-tight ${
              plan.highlighted ? "text-5xl xl:text-6xl" : "text-4xl"
            }`}
            style={
              plan.highlighted
                ? {
                    background: `linear-gradient(135deg, ${accentColor}, #E8C97D)`,
                    WebkitBackgroundClip: "text",
                    WebkitTextFillColor: "transparent",
                    backgroundClip: "text",
                  }
                : { color: "#FAFAFA" }
            }
            initial={reduced ? false : { opacity: 0, scale: 0.85 }}
            animate={reduced || inView ? { opacity: 1, scale: 1 } : { opacity: 0, scale: 0.85 }}
            transition={{
              duration: 0.6,
              delay: 0.3 + index * 0.12,
              ease: [0.25, 0.46, 0.45, 0.94],
            }}
          >
            {plan.price}
          </motion.div>
          <p className="mt-2 text-xs text-white/35 sm:text-sm">
            {plan.priceNote}
            {plan.discountEndsHint ? ` · акция ${plan.discountEndsHint}` : ""}
          </p>
        </div>

        {/* Features list — stagger reveal */}
        <ul className="mt-6 flex-1 space-y-3.5">
          {plan.features.map((feature, fIdx) => (
            <motion.li
              key={feature.text}
              className="flex items-start gap-3"
              initial={reduced ? false : { opacity: 0, x: -8 }}
              animate={reduced || inView ? { opacity: 1, x: 0 } : { opacity: 0, x: -8 }}
              transition={{
                duration: 0.4,
                delay: 0.4 + index * 0.12 + fIdx * 0.04,
                ease: "easeOut",
              }}
            >
              <Icons.check
                className="mt-0.5 size-4 shrink-0"
                style={{
                  color: feature.highlight || isGold ? accentColor : "rgba(107,173,165,0.7)",
                }}
              />
              <span
                className={`text-sm leading-relaxed xl:text-[15px] ${
                  feature.highlight ? "font-medium text-white/85" : "text-white/65"
                }`}
              >
                {feature.text}
              </span>
            </motion.li>
          ))}
        </ul>

        {/* CTA */}
        <Link
          href={plan.ctaHref}
          className={`mt-8 flex w-full items-center justify-center gap-2 rounded-xl px-6 py-3.5 text-sm font-semibold transition-all duration-300 ${
            plan.highlighted
              ? "shadow-[0_0_30px_rgba(201,168,76,0.2)] hover:shadow-[0_0_40px_rgba(201,168,76,0.4)]"
              : "hover:shadow-[0_0_24px_rgba(107,173,165,0.18)]"
          }`}
          style={
            plan.highlighted
              ? {
                  background: `linear-gradient(135deg, ${accentColor}, #E8C97D)`,
                  color: "#0A0A0B",
                }
              : plan.accent === "neutral"
                ? {
                    background: "rgba(255,255,255,0.06)",
                    color: "rgba(255,255,255,0.85)",
                    border: "1px solid rgba(255,255,255,0.1)",
                  }
                : {
                    background: accentColor,
                    color: "#0A0A0B",
                  }
          }
        >
          {plan.cta}
          <Icons.arrowRight className="size-4 transition-transform group-hover:translate-x-0.5" />
        </Link>
      </div>
    </motion.article>
  );
}

// ---------------------------------------------------------------------------
// TrustItem — small trust signal under cards
// ---------------------------------------------------------------------------

function TrustItem({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-[#6BADA5]/60">{icon}</span>
      <span>{label}</span>
    </div>
  );
}
