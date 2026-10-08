"use client";

import type { LevelTestAttemptTeaserDto } from "@/entities/level-test";
import { trackGrowthEvent } from "@/shared/analytics";
import { routes } from "@/shared/config/routes";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";
import { LevelTestScoreHeader } from "./level-test-score-header";

/** Фейковые «секции» для замазанного превью — реальных данных у тизера нет физически. */
const BLURRED_PREVIEW_ROWS = [72, 45, 88, 33, 60];

interface LevelTestResultTeaserProps {
  teaser: LevelTestAttemptTeaserDto;
  /**
   * Auth-юзер может застрять на тизере (чужая неклеймленная попытка) —
   * логин-CTA ему бесполезна, показываем нейтральную подпись.
   */
  viewerAuthenticated: boolean;
  isClaiming: boolean;
}

/**
 * Lead-gated тизер результата: крупный % + уровень + ЗАМАЗАННЫЙ превью-блок
 * секций (CSS blur на фейковых барах) + CTA «Войти и получить полный разбор»
 * с возвратом на эту же страницу. Issue #481.
 */
export function LevelTestResultTeaser({
  teaser,
  viewerAuthenticated,
  isClaiming,
}: LevelTestResultTeaserProps) {
  const loginHref = `${routes.login}?callbackUrl=${encodeURIComponent(
    routes.levelTestResult(teaser.attemptId),
  )}`;

  return (
    <div className="mx-auto w-full max-w-2xl space-y-8 py-10">
      <LevelTestScoreHeader
        overallPercent={teaser.overallPercent}
        level={teaser.level}
        subtitle={`Отвечено ${teaser.answeredCount} из ${teaser.totalQuestions} вопросов`}
      />

      {/* Замазанный превью-блок: фейковые секционные бары под blur'ом + CTA поверх */}
      <div className="relative overflow-hidden rounded-xl border border-border/60 bg-card">
        <div className="space-y-4 p-6 blur-[6px] select-none" aria-hidden inert>
          {BLURRED_PREVIEW_ROWS.map((width, index) => (
            <div key={index} className="space-y-1.5">
              <div className="flex items-center justify-between">
                <div className="h-3 w-32 rounded bg-muted-foreground/30" />
                <div className="h-3 w-10 rounded bg-muted-foreground/20" />
              </div>
              <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
                <div className="h-full rounded-full bg-primary/50" style={{ width: `${width}%` }} />
              </div>
            </div>
          ))}
        </div>

        <div className="absolute inset-0 flex items-center justify-center bg-gradient-to-b from-background/30 via-background/60 to-background/80 p-6">
          <div className="flex max-w-sm flex-col items-center gap-3 text-center">
            <Icons.locked className="size-6 text-muted-foreground" aria-hidden />
            <p className="font-semibold">Полный разбор скрыт</p>
            <p className="text-sm text-muted-foreground">
              Проценты по темам, слабые места, оценка развёрнутых ответов и рекомендация курса
            </p>
            {viewerAuthenticated ? (
              isClaiming ? (
                <p className="flex items-center gap-2 text-sm text-muted-foreground">
                  <Icons.loading className="size-4 animate-spin" aria-hidden />
                  Привязываем результат…
                </p>
              ) : (
                <p className="text-sm text-muted-foreground">
                  Полный разбор доступен владельцу попытки
                </p>
              )
            ) : (
              <Button asChild>
                <Link
                  href={loginHref}
                  prefetch={false}
                  onClick={() =>
                    trackGrowthEvent({
                      name: "level_test_auth_started",
                      properties: {},
                    })
                  }
                >
                  Войти и получить полный разбор
                </Link>
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
