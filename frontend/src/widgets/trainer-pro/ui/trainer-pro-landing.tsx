"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { formatPriceFromCents, hasTrainerProGrant, myGrantsQueryOptions } from "@/entities/access-plan";
import { resolveTrainerProOfferCard, trainerProOfferQueryOptions } from "@/entities/trainer-pro";
import { BuyTrainerProButton } from "@/features/buy-trainer-pro";
import { useIsAuthenticated, useRoles } from "@/shared/auth";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Skeleton } from "@/shared/ui/kit/skeleton";

/**
 * Лендинг подписки «Тренажёр Pro» (#623) — отдельная страница в пространстве
 * тренажёра (route-группа `(trainer)` → фиолетовая тема + `TrainerSidebar`).
 * Тренажёр продаётся ОТДЕЛЬНО от платформенного `/pricing`: здесь объясняется,
 * что такое тренажёр и что входит в подписку. Оффер тянется из выделенного
 * trainer-API (`GET /access/trainer-pro/offer/`), checkout — `BuyTrainerProButton`
 * (POST `/access/trainer-pro/orders/`; сам ведёт анонима на логин и инициирует оплату
 * server-side). Платформенный каталог планов тренажёрный план больше не содержит (#674).
 */

const FEATURES: ReadonlyArray<{ icon: IconComponent; title: string; desc: string }> = [
  {
    icon: Icons.mic,
    title: "Голосовые ответы",
    desc: "Отвечай голосом — Whisper расшифрует, AI оценит формулировки как на реальном собесе.",
  },
  {
    icon: Icons.briefcase,
    title: "Мок-интервью",
    desc: "Полная симуляция собеседования: набор вопросов, разбор ответов и итоговый фидбэк.",
  },
  {
    icon: Icons.message,
    title: "Развёрнутые ответы",
    desc: "Открытые вопросы с AI-проверкой: балл, что улучшить и эталонный ответ.",
  },
  {
    icon: Icons.restore,
    title: "Умные повторы",
    desc: "Интервальное повторение возвращает забытое ровно тогда, когда пора освежить.",
  },
  {
    icon: Icons.library,
    title: "Все темы и банки",
    desc: "Полный доступ к платным банкам вопросов по всем направлениям подготовки.",
  },
  {
    icon: Icons.unlocked,
    title: "Без лимитов",
    desc: "Без дневных ограничений на AI-проверки, голосовые ответы и симуляции.",
  },
];

export function TrainerProLanding() {
  const isAuthenticated = useIsAuthenticated();
  const { isAtLeast } = useRoles();
  const isAdmin = isAtLeast("platform-admin");

  const { data: offers, isLoading: offersLoading } = useQuery(trainerProOfferQueryOptions());
  const { data: grants } = useQuery({
    ...myGrantsQueryOptions(),
    enabled: isAuthenticated && !isAdmin,
  });

  const offer = resolveTrainerProOfferCard(offers);
  const hasPro = isAdmin || (grants ? hasTrainerProGrant(grants) : false);
  const cadence = offer?.monthly ? "/мес" : "";
  const priceLabel = offer
    ? `${formatPriceFromCents(offer.priceCents, offer.currency)}${cadence}`
    : null;

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-8 sm:px-6 sm:py-12">
      {/* HERO — оффер + цена + CTA */}
      <section
        className={cn(
          "relative overflow-hidden rounded-3xl border border-violet-500/30 p-6 shadow-sm sm:p-9",
          "bg-gradient-to-br from-violet-500/[0.10] via-card to-card",
        )}
      >
        <div
          aria-hidden="true"
          className="pointer-events-none absolute -right-24 -top-24 size-72 rounded-full bg-violet-500/15 blur-3xl"
        />
        <div className="relative grid gap-8 lg:grid-cols-[1.1fr_0.9fr] lg:items-center">
          <div className="min-w-0">
            <span className="inline-flex w-fit items-center gap-1.5 rounded-full border border-violet-500/30 bg-violet-500/10 px-3 py-1 text-[11px] font-semibold uppercase tracking-wider text-violet-600 dark:text-violet-300">
              <Icons.energy className="size-3.5" />
              Тренажёр Pro
            </span>
            <h1 className="mt-4 text-3xl font-bold tracking-tight sm:text-4xl">
              Открой тренажёр без лимитов
            </h1>
            <p className="mt-4 max-w-xl text-pretty text-sm leading-relaxed text-muted-foreground sm:text-base">
              Голосовые ответы, мок-интервью, развёрнутые ответы с AI-разбором, умные
              повторы и все банки вопросов. Готовься к собеседованию так, как будто оно
              уже идёт.
            </p>

            <div className="mt-7">
              {offersLoading ? (
                <Skeleton className="h-12 w-64 rounded-xl" />
              ) : hasPro ? (
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                  <span className="inline-flex items-center gap-2 rounded-xl border border-violet-500/30 bg-violet-500/10 px-4 py-2.5 text-sm font-medium text-violet-700 dark:text-violet-200">
                    <Icons.check className="size-4" />
                    {isAdmin ? "У тебя есть доступ ко всему тренажёру" : "Подписка активна"}
                  </span>
                  <Button asChild className="bg-violet-600 text-white hover:bg-violet-600/90">
                    <Link href={routes.trainer}>Открыть тренажёр</Link>
                  </Button>
                </div>
              ) : offer && priceLabel ? (
                <div className="flex flex-col gap-2">
                  <BuyTrainerProButton
                    planId={offer.planId}
                    priceCents={offer.priceCents}
                    currency={offer.currency}
                    size="lg"
                    className="w-full bg-violet-600 text-white hover:bg-violet-600/90 sm:w-auto"
                    label={`Оформить Тренажёр Pro — ${priceLabel}`}
                  />
                  <p className="text-xs text-muted-foreground">
                    {cadence
                      ? "Подписка с автопродлением — отменить можно в любой момент."
                      : "Разовая оплата доступа к тренажёру."}
                  </p>
                  {offer.features.length > 0 && (
                    <ul className="mt-1 space-y-1.5 text-sm">
                      {offer.features.map((feature) => (
                        <li key={feature} className="flex items-start gap-2">
                          <Icons.check className="mt-0.5 size-4 shrink-0 text-violet-500 dark:text-violet-300" />
                          <span className="text-foreground/85">{feature}</span>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              ) : (
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
                  <span className="text-sm text-muted-foreground">
                    Подписка скоро появится.
                  </span>
                  <Button asChild variant="outline">
                    <Link href={routes.trainer}>Открыть тренажёр</Link>
                  </Button>
                </div>
              )}
            </div>
          </div>

          {/* Бесплатно vs Pro — короткое сравнение */}
          <div className="rounded-2xl border border-border/60 bg-background/40 p-5 backdrop-blur-sm sm:p-6">
            <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Что меняется с Pro
            </p>
            <ul className="mt-4 space-y-3 text-sm">
              <CompareRow label="Закрытые тесты (выбор ответа)" free pro />
              <CompareRow label="Развёрнутые ответы + AI-разбор" pro />
              <CompareRow label="Голосовые ответы" pro />
              <CompareRow label="Мок-интервью" pro />
              <CompareRow label="Все банки вопросов" pro />
              <CompareRow label="Без дневных лимитов" pro />
            </ul>
          </div>
        </div>
      </section>

      {/* FEATURES */}
      <section className="mt-10 sm:mt-14">
        <h2 className="text-xl font-bold tracking-tight sm:text-2xl">Что входит в подписку</h2>
        <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map(({ icon: Icon, title, desc }) => (
            <div
              key={title}
              className="rounded-2xl border border-border/60 bg-card p-5 transition-colors hover:border-violet-500/30"
            >
              <span className="inline-flex size-9 items-center justify-center rounded-xl bg-violet-500/12 text-violet-500 dark:text-violet-300">
                <Icon className="size-5" />
              </span>
              <h3 className="mt-3 text-sm font-semibold">{title}</h3>
              <p className="mt-1 text-sm leading-relaxed text-muted-foreground">{desc}</p>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

/** Строка сравнения «бесплатно / Pro» — галочка в доступных колонках. */
function CompareRow({ label, free, pro }: { label: string; free?: boolean; pro?: boolean }) {
  return (
    <li className="flex items-center justify-between gap-3">
      <span className="min-w-0 flex-1 text-foreground/80">{label}</span>
      <span className="flex shrink-0 items-center gap-3">
        <Mark on={free} title="Бесплатно" />
        <Mark on={pro} title="Pro" violet />
      </span>
    </li>
  );
}

function Mark({ on, title, violet }: { on?: boolean; title: string; violet?: boolean }) {
  return (
    <span className="flex w-9 justify-center" title={title}>
      {on ? (
        <Icons.check className={cn("size-4", violet ? "text-violet-500 dark:text-violet-300" : "text-green")} />
      ) : (
        <span aria-hidden="true" className="text-muted-foreground/40">
          —
        </span>
      )}
    </span>
  );
}
