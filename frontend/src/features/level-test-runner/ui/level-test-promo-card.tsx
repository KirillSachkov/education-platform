"use client";

import { levelTestQueryOptions } from "@/entities/level-test";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { DEVELOPER_LEVEL_LABELS } from "@/shared/types";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useSession } from "next-auth/react";

/** Лесенка 6 уровней «Новичок → Senior»: высота и насыщенность растут к Senior. */
const LADDER_STEPS = [
  { height: 16, barClass: "bg-primary/20" },
  { height: 24, barClass: "bg-primary/30" },
  { height: 33, barClass: "bg-primary/40" },
  { height: 42, barClass: "bg-primary/55" },
  { height: 52, barClass: "bg-primary/70" },
  { height: 64, barClass: "bg-primary/90" },
] as const;

/**
 * Промо-карточка теста уровня для главной (#528): мини-версия эстетики
 * лендинга воронки (mono-оверлайн, dot-grid, glow) + лесенка из шести
 * ступеней шкалы — карточка показывает сам продукт, а не абстрактную иконку.
 * Прошедшим тест не показывается (фидбек владельца): пока ответ my-latest
 * не получен — рендерим null, чтобы карточка не мигала.
 */
export function LevelTestPromoCard() {
  const session = useSession();
  const userId = session.status === "authenticated" ? (session.data.user.id ?? null) : null;
  const isAuthenticated = typeof userId === "string";
  const viewerScope = typeof userId === "string" ? `user:${userId}` : `session:${session.status}`;
  const latestQuery = useQuery({
    ...levelTestQueryOptions.myLatestAttemptOptions(viewerScope),
    enabled: isAuthenticated,
  });

  if (!isAuthenticated || latestQuery.isPending || latestQuery.data) {
    return null;
  }

  return (
    <section className="relative overflow-hidden rounded-xl border border-border/60 bg-card">
      {/* Глубина: glow за лесенкой + угасающий dot-grid в правой половине */}
      <div aria-hidden className="pointer-events-none absolute inset-0">
        <div className="absolute -right-12 -top-20 size-64 rounded-full bg-primary/[0.09] blur-3xl" />
        <div className="absolute inset-y-0 right-0 hidden w-1/2 bg-[radial-gradient(circle,color-mix(in_srgb,var(--foreground)_7%,transparent)_1px,transparent_1px)] [background-size:18px_18px] [mask-image:linear-gradient(to_left,black,transparent)] sm:block" />
      </div>

      <div className="relative flex flex-col gap-5 p-5 sm:flex-row sm:items-center sm:justify-between sm:gap-10 sm:p-6">
        <div className="min-w-0">
          <p className="font-mono text-xs text-primary">
            <span className="text-muted-foreground/70">{"//"}</span> бесплатный тест · ~20 минут
          </p>
          <h2 className="mt-1.5 text-balance text-lg font-semibold leading-snug">
            Определи свой уровень .NET-разработчика
          </h2>
          <p className="mt-1 max-w-xl text-sm text-muted-foreground">
            Вопросы от основ C# до архитектуры — уровень по шкале из 6 ступеней, слабые места и с
            чего начать рост.
          </p>
          <Button asChild className="mt-4">
            <Link href={routes.levelTest}>
              Пройти тест
              <Icons.arrowRight className="size-4" />
            </Link>
          </Button>
        </div>

        {/* Лесенка уровней — визуальный мотив шкалы «Новичок → Senior» */}
        <div aria-hidden className="hidden shrink-0 select-none pr-1 sm:block">
          <div className="flex items-end gap-1.5">
            {LADDER_STEPS.map((step, index) => (
              <div
                key={step.height}
                className={cn("relative w-6 rounded-t-md", step.barClass)}
                style={{ height: step.height }}
              >
                {index === LADDER_STEPS.length - 1 && (
                  <span className="absolute -top-1 left-1/2 size-2 -translate-x-1/2 rounded-full bg-primary shadow-[0_0_10px_2px] shadow-primary/50 motion-safe:animate-pulse" />
                )}
              </div>
            ))}
          </div>
          <div className="mt-2 flex justify-between font-mono text-[10px] text-muted-foreground">
            <span>{DEVELOPER_LEVEL_LABELS.PRE_JUNIOR}</span>
            <span className="text-primary">{DEVELOPER_LEVEL_LABELS.SENIOR}</span>
          </div>
        </div>
      </div>
    </section>
  );
}
