"use client";

import {
  isAiGradingPending,
  type LevelTestAttemptResult,
  type LevelTestStudentDto,
} from "@/entities/level-test";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { DEVELOPER_LEVEL_LABELS } from "@/shared/types";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import Link from "next/link";
import { DEVELOPER_LEVEL_BADGE_CLASSES } from "../model/level-visuals";

interface LevelTestLandingHeroProps {
  test: LevelTestStudentDto;
  lastAttemptId: string | null;
  /** Последняя попытка auth-юзера (#528): лендинг показывает результат и разбор. */
  latestResult: LevelTestAttemptResult | null;
  /** Есть сохранённый черновик прохождения — CTA продолжает, а не начинает заново. */
  hasDraft: boolean;
  onStart: () => void;
}

const STEPS = [
  {
    tag: "вопросы",
    title: "Отвечаешь на вопросы",
    text: "Выбор ответа, задачи «что выведет код» и развёрнутые вопросы — отвечай на то, что знаешь.",
  },
  {
    tag: "проверка",
    title: "Полная проверка ответов",
    text: "Проверяется каждый ответ, включая развёрнутые вопросы.",
  },
  {
    tag: "результат",
    title: "Получаешь уровень и план",
    text: "Точный уровень, разбор по темам и рекомендация, что подтянуть в первую очередь.",
  },
] as const;

// Статичный мок результата — чисто декоративный «как это будет выглядеть».
const PREVIEW_BARS = [
  { title: "Основы C#", percent: 86, barClass: "bg-emerald-500/80" },
  { title: "Асинхронность", percent: 61, barClass: "bg-amber-500/80" },
  { title: "Архитектура", percent: 38, barClass: "bg-violet-500/80" },
] as const;

/**
 * Компонентные стили лендинга: атмосфера (dot-grid, зерно), entrance-стэггер,
 * blink-курсор, float-чипы и scroll-reveal как progressive enhancement
 * (@supports animation-timeline). Все анимации за prefers-reduced-motion.
 */
const LANDING_CSS = `
.lt-grid {
  background-image: radial-gradient(
    circle,
    color-mix(in srgb, var(--foreground) 7%, transparent) 1px,
    transparent 1px
  );
  background-size: 26px 26px;
  mask-image: radial-gradient(ellipse 70% 60% at 50% 0%, black 25%, transparent 70%);
}
@media (prefers-reduced-motion: no-preference) {
  .lt-rise {
    animation: lt-rise 0.7s cubic-bezier(0.22, 1, 0.36, 1) both;
    animation-delay: var(--d, 0ms);
  }
  .lt-cursor {
    animation: lt-blink 1.1s steps(2, start) infinite;
  }
  @supports (animation-timeline: view()) {
    .lt-reveal {
      animation: lt-rise 1s cubic-bezier(0.22, 1, 0.36, 1) both;
      animation-timeline: view();
      animation-range: entry 0% entry 60%;
    }
  }
}
@keyframes lt-rise {
  from {
    opacity: 0;
    translate: 0 22px;
  }
  to {
    opacity: 1;
    translate: 0 0;
  }
}
@keyframes lt-blink {
  50% {
    opacity: 0;
  }
}
`;

/** Токены ручной подсветки C#-сниппета — читаемы в обеих темах. */
const TOKEN = {
  kw: "text-teal-700 dark:text-teal-300",
  type: "text-violet-600 dark:text-violet-300",
  num: "text-amber-600 dark:text-amber-300",
  method: "text-sky-700 dark:text-sky-300",
  comment: "text-muted-foreground/70 italic",
  dim: "text-foreground/65",
} as const;

/**
 * Окно редактора с тизер-вопросом «что выведет код?» — визуальный материал
 * hero: лендинг показывает сам продукт (вопросы теста), а не абстракцию.
 */
function CodeWindow() {
  const lines: React.ReactNode[] = [
    <>
      <span className={TOKEN.kw}>var</span> <span className={TOKEN.dim}>tasks</span> ={" "}
      <span className={TOKEN.type}>Enumerable</span>.<span className={TOKEN.method}>Range</span>(
      <span className={TOKEN.num}>1</span>, <span className={TOKEN.num}>3</span>)
    </>,
    <>
      {"  "}.<span className={TOKEN.method}>Select</span>(<span className={TOKEN.kw}>async</span>{" "}
      <span className={TOKEN.dim}>i</span> =&gt; {"{"}
    </>,
    <>
      {"    "}
      <span className={TOKEN.kw}>await</span> <span className={TOKEN.type}>Task</span>.
      <span className={TOKEN.method}>Delay</span>(<span className={TOKEN.num}>10</span> *{" "}
      <span className={TOKEN.dim}>i</span>);
    </>,
    <>
      {"    "}
      <span className={TOKEN.kw}>return</span> <span className={TOKEN.dim}>i</span> *{" "}
      <span className={TOKEN.dim}>i</span>;
    </>,
    <>{"  })"};</>,
    <> </>,
    <>
      <span className={TOKEN.kw}>var</span> <span className={TOKEN.dim}>results</span> ={" "}
      <span className={TOKEN.kw}>await</span> <span className={TOKEN.type}>Task</span>.
      <span className={TOKEN.method}>WhenAll</span>(<span className={TOKEN.dim}>tasks</span>);
    </>,
    <>
      <span className={TOKEN.type}>Console</span>.<span className={TOKEN.method}>WriteLine</span>(
      <span className={TOKEN.type}>string</span>.<span className={TOKEN.method}>Join</span>(
      <span className={TOKEN.num}>&quot;, &quot;</span>, <span className={TOKEN.dim}>results</span>
      ));
    </>,
  ];

  return (
    <div className="relative">
      {/* Подложка-двойник: смещённая панель добавляет стопке глубину. */}
      <div
        aria-hidden
        className="absolute -inset-1 translate-x-3 translate-y-4 rotate-[1.6deg] rounded-2xl border border-border/50 bg-card/30"
      />

      <div className="relative overflow-hidden rounded-2xl border border-border/80 bg-gradient-to-b from-card to-card/70 shadow-[0_24px_70px_-28px_rgba(0,0,0,0.55)] backdrop-blur-sm">
        {/* Хром окна */}
        <div className="flex items-center gap-2 border-b border-border/60 bg-background/40 px-4 py-2.5">
          <span aria-hidden className="flex gap-1.5">
            <i className="size-2.5 rounded-full bg-red-400/60" />
            <i className="size-2.5 rounded-full bg-amber-400/60" />
            <i className="size-2.5 rounded-full bg-emerald-400/60" />
          </span>
          <span className="ml-2 font-mono text-xs text-muted-foreground">level-check.csx</span>
          <span className="ml-auto font-mono text-[10px] uppercase tracking-wider text-muted-foreground/60">
            C# · .NET
          </span>
        </div>

        {/* Сниппет */}
        <pre className="overflow-x-auto px-4 py-4 font-mono text-[12.5px] leading-[1.75] sm:text-[13px]">
          {lines.map((line, index) => (
            <div key={index} className="flex">
              <span
                aria-hidden
                className="w-7 shrink-0 select-none pr-3 text-right text-muted-foreground/35"
              >
                {index + 1}
              </span>
              <code>{line}</code>
            </div>
          ))}
        </pre>

        {/* Промпт — мостик от артефакта к продукту */}
        <div className="flex items-baseline gap-2 border-t border-border/60 bg-background/40 px-4 py-3 font-mono text-xs">
          <span className="text-primary">❯</span>
          <span className="text-foreground/85">что выведет этот код?</span>
          <span
            aria-hidden
            className="lt-cursor inline-block h-3.5 w-[7px] self-center bg-primary/80"
          />
          <span className="ml-auto hidden text-muted-foreground/60 sm:inline">
            узнаешь внутри теста
          </span>
        </div>
      </div>
    </div>
  );
}

/**
 * Лендинг воронки `/level-test` (intro-состояние funnel'а) в эстетике
 * «IDE-нуар»: асимметричный hero с подсвеченным C#-сниппетом в окне редактора,
 * статус-бар фактов, темы деревом Solution Explorer (с числом вопросов на тему
 * из живого DTO), шаги-pipeline и спокойный CTA. Глубина — слоями (dot-grid,
 * glow, стопки карточек), но в палитре платформы — без цветовых пятен по краям
 * и без упоминаний AI (#527, фидбек владельца).
 */
export function LevelTestLandingHero({
  test,
  lastAttemptId,
  latestResult,
  hasDraft,
  onStart,
}: LevelTestLandingHeroProps) {
  const ctaLabel = hasDraft ? "Продолжить тест" : latestResult ? "Пройти ещё раз" : "Начать тест";
  const facts = [
    { value: String(test.totalQuestions), label: "вопросов", dotClass: "bg-primary" },
    { value: "~20", label: "минут", dotClass: "bg-amber-400/80" },
    { value: "100%", label: "проверка ответов", dotClass: "bg-violet-400/80" },
    ...(test.sections.length > 0
      ? [
          {
            value: String(test.sections.length),
            label: "тем с уровнем",
            dotClass: "bg-emerald-400/80",
          },
        ]
      : []),
  ];

  return (
    <div className="relative">
      {/* Статическая константа модуля, не пользовательский ввод. href+precedence
          включают React 19 style-hoisting в head с дедупликацией при ремаунтах. */}
      <style href="level-test-landing" precedence="default">
        {LANDING_CSS}
      </style>

      {/* Атмосфера: сетка + одно мягкое glow-пятно сверху по центру. Fixed на всю
          ширину вьюпорта — фон не обрывается на границах контент-колонки
          (на мобильном были резкие вертикальные переходы по краям). */}
      <div
        aria-hidden
        className="pointer-events-none fixed inset-x-0 top-0 -z-10 h-[480px] overflow-hidden"
      >
        <div className="lt-grid absolute inset-0" />
        <div className="absolute left-1/2 top-[-240px] h-[440px] w-[640px] -translate-x-1/2 rounded-full bg-primary/10 blur-3xl" />
      </div>

      {/* Hero — редакционная асимметрия: тип-стек слева, артефакт справа */}
      <section className="mx-auto grid w-full max-w-6xl grid-cols-1 items-center gap-12 px-4 pb-16 pt-12 sm:pt-16 lg:grid-cols-[minmax(0,1.05fr)_minmax(0,0.95fr)] lg:gap-8 lg:pb-20">
        <div className="flex max-w-xl flex-col items-start gap-6">
          <p
            className="lt-rise flex items-center gap-2 font-mono text-[13px] text-primary"
            style={{ "--d": "0ms" } as React.CSSProperties}
          >
            <span className="text-muted-foreground/70">{"//"}</span>
            бесплатный тест уровня
            <span aria-hidden className="lt-cursor inline-block h-3.5 w-[7px] bg-primary/80" />
          </p>

          <h1
            className="lt-rise text-balance text-4xl font-bold leading-[1.05] tracking-tight sm:text-5xl xl:text-6xl"
            style={{ "--d": "80ms" } as React.CSSProperties}
          >
            Определи свой уровень{" "}
            <span className="bg-gradient-to-r from-primary via-teal-600 to-violet-500 bg-clip-text text-transparent dark:via-teal-300 dark:to-violet-400 dark:[text-shadow:0_0_50px_color-mix(in_srgb,var(--primary)_35%,transparent)]">
              .NET-разработчика
            </span>
          </h1>

          <p
            className="lt-rise text-pretty text-base text-muted-foreground sm:text-lg"
            style={{ "--d": "160ms" } as React.CSSProperties}
          >
            Ответь на вопросы по C# и .NET — получишь точную оценку уровня и разбор по темам.
          </p>

          <div
            className="lt-rise flex flex-col items-start gap-3"
            style={{ "--d": "240ms" } as React.CSSProperties}
          >
            <Button
              size="lg"
              onClick={onStart}
              className="group relative h-12 overflow-hidden px-8 text-base shadow-[0_0_44px_-10px] shadow-primary/50"
            >
              {/* Шайн-свип по ховеру */}
              <span
                aria-hidden
                className="absolute inset-y-0 left-[-60%] w-1/2 -skew-x-12 bg-white/20 transition-[left] duration-500 ease-out group-hover:left-[120%]"
              />
              {ctaLabel}
              <Icons.arrowRight className="size-4 transition-transform group-hover:translate-x-0.5" />
            </Button>
            {latestResult ? (
              <div className="flex w-full max-w-md flex-col gap-2.5 rounded-xl border border-border/60 bg-card/60 p-4 backdrop-blur-sm">
                <p className="font-mono text-xs text-muted-foreground">
                  <span className="text-muted-foreground/70">{"//"}</span> твой последний результат
                </p>
                {isAiGradingPending(latestResult.aiGradingStatus) ? (
                  <div className="flex flex-wrap items-center gap-2.5 text-sm text-muted-foreground">
                    <Icons.loading className="size-4 animate-spin text-primary" aria-hidden />
                    Ответы проверяются — результат появится в течение минуты.
                    <Link
                      href={routes.levelTestResult(latestResult.attemptId)}
                      className="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline max-sm:basis-full sm:ml-auto"
                      prefetch={false}
                    >
                      Открыть
                      <Icons.arrowRight className="size-3.5" />
                    </Link>
                  </div>
                ) : (
                  <div className="flex flex-wrap items-center gap-2.5">
                    <span
                      className={cn(
                        "rounded-md border px-2 py-0.5 text-sm font-medium",
                        DEVELOPER_LEVEL_BADGE_CLASSES[latestResult.level],
                      )}
                    >
                      {DEVELOPER_LEVEL_LABELS[latestResult.level]}
                    </span>
                    <span className="font-mono text-sm tabular-nums text-foreground/85">
                      {latestResult.overallPercent}%
                    </span>
                    <Link
                      href={routes.levelTestResult(latestResult.attemptId)}
                      className="inline-flex items-center gap-1 text-sm text-primary underline-offset-4 hover:underline max-sm:basis-full sm:ml-auto"
                      prefetch={false}
                    >
                      Разбор и рекомендации
                      <Icons.arrowRight className="size-3.5" />
                    </Link>
                  </div>
                )}
              </div>
            ) : (
              lastAttemptId && (
                <Link
                  href={routes.levelTestResult(lastAttemptId)}
                  className="text-sm text-muted-foreground underline-offset-4 hover:text-foreground hover:underline"
                  prefetch={false}
                >
                  Посмотреть последний результат
                </Link>
              )
            )}
          </div>
        </div>

        <div className="lt-rise min-w-0 lg:pl-4" style={{ "--d": "320ms" } as React.CSSProperties}>
          <CodeWindow />
        </div>
      </section>

      {/* Статус-бар фактов — одна моно-строка вместо четырёх одинаковых плиток */}
      <section className="lt-reveal border-y border-border/50 bg-card/30 backdrop-blur-sm">
        <dl className="mx-auto flex w-full max-w-5xl flex-wrap items-center justify-center gap-x-8 gap-y-2 px-4 py-3.5 font-mono text-sm">
          {facts.map((fact) => (
            <div key={fact.label} className="flex items-baseline gap-2">
              <span
                aria-hidden
                className={cn("size-1.5 self-center rounded-full", fact.dotClass)}
              />
              <dd className="font-semibold tabular-nums text-foreground">{fact.value}</dd>
              <dt className="text-xs text-muted-foreground">{fact.label}</dt>
            </div>
          ))}
        </dl>
      </section>

      {/* Темы — дерево Solution Explorer вместо сетки бордер-ячеек */}
      {test.sections.length > 0 && (
        <section className="lt-reveal mx-auto w-full max-w-4xl px-4 pb-20 pt-16">
          <p className="mb-2 font-mono text-[13px] text-primary">
            <span className="text-muted-foreground/70">{"//"}</span> что проверяем
          </p>
          <h2 className="mb-6 text-2xl font-bold tracking-tight">От синтаксиса до архитектуры</h2>

          <div className="rounded-2xl border border-border/60 bg-gradient-to-b from-card/80 to-card/40 p-5 shadow-[0_18px_50px_-30px_rgba(0,0,0,0.5)] backdrop-blur-sm sm:p-6">
            <p className="mb-3 flex items-center gap-2 font-mono text-xs text-muted-foreground/70">
              <Icons.fileCode className="size-3.5" aria-hidden />
              level-test/themes/
            </p>
            <ul className="font-mono text-sm sm:columns-2 sm:gap-10">
              {test.sections.map((section, index) => {
                const questionCount = test.questions.filter(
                  (question) => question.section === section.key,
                ).length;
                return (
                  <li key={section.key} className="break-inside-avoid">
                    <span className="group flex items-center gap-2.5 rounded-md px-2 py-1.5 transition-colors hover:bg-accent">
                      <span aria-hidden className="text-muted-foreground/40">
                        ├──
                      </span>
                      <span className="text-xs tabular-nums text-primary/70">
                        {String(index + 1).padStart(2, "0")}
                      </span>
                      <span
                        className="truncate text-foreground/85 transition-transform group-hover:translate-x-0.5"
                        title={section.title}
                      >
                        {section.title}
                      </span>
                      {questionCount > 0 && (
                        <span className="ml-auto shrink-0 text-xs tabular-nums text-muted-foreground/55">
                          {questionCount}
                        </span>
                      )}
                    </span>
                  </li>
                );
              })}
              <li aria-hidden className="break-inside-avoid">
                <span className="flex items-center gap-2.5 px-2 py-1.5 text-muted-foreground/50">
                  <span>└──</span>
                  <span className="truncate italic">result.md — узнаешь после теста</span>
                </span>
              </li>
            </ul>
          </div>
        </section>
      )}

      {/* Как это работает (pipeline) + превью результата */}
      <section className="lt-reveal mx-auto grid w-full max-w-5xl grid-cols-1 items-center gap-12 px-4 pb-20 lg:grid-cols-[minmax(0,1fr)_minmax(0,380px)]">
        <div>
          <p className="mb-2 font-mono text-[13px] text-primary">
            <span className="text-muted-foreground/70">{"//"}</span> как это работает
          </p>
          <h2 className="mb-8 text-2xl font-bold tracking-tight">Три шага до твоего уровня</h2>

          <ol className="flex flex-col gap-7">
            {STEPS.map((step, index) => (
              <li key={step.title} className="relative flex gap-4">
                {index < STEPS.length - 1 && (
                  <span
                    aria-hidden
                    className="absolute left-[15px] top-9 h-[calc(100%-14px)] w-px bg-gradient-to-b from-primary/40 to-border/60"
                  />
                )}
                <span className="flex size-8 shrink-0 items-center justify-center rounded-full border border-primary/30 bg-primary/10 font-mono text-sm font-semibold text-primary shadow-[0_0_18px_-4px] shadow-primary/40">
                  {index + 1}
                </span>
                <div className="min-w-0 pt-0.5">
                  <h3 className="flex flex-wrap items-baseline gap-x-2.5 font-medium">
                    {step.title}
                    <span className="font-mono text-[11px] font-normal text-muted-foreground/60">
                      {step.tag}
                    </span>
                  </h3>
                  <p className="mt-1 text-sm text-muted-foreground">{step.text}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>

        {/* Декоративный мок результата — «стопка» отчётов с глубиной */}
        <div aria-hidden className="relative mx-auto w-full max-w-sm select-none">
          <div className="absolute -inset-6 -z-10 rounded-[2rem] bg-primary/10 blur-2xl" />
          <div className="absolute inset-0 translate-x-4 translate-y-3 rotate-[2.5deg] rounded-2xl border border-border/50 bg-card/40" />
          <div className="relative rotate-[-1.5deg] rounded-2xl border border-border/70 bg-card/90 p-5 shadow-[0_30px_70px_-30px_rgba(0,0,0,0.6)] backdrop-blur transition-transform duration-300 motion-safe:hover:rotate-0">
            <div className="flex items-center justify-between">
              <span className="font-mono text-xs text-muted-foreground">Твой результат</span>
              <span
                className={cn(
                  "rounded-md border px-2 py-0.5 text-xs font-medium",
                  DEVELOPER_LEVEL_BADGE_CLASSES.MIDDLE,
                )}
              >
                Middle
              </span>
            </div>
            <div className="mt-4 flex flex-col gap-3">
              {PREVIEW_BARS.map((bar) => (
                <div key={bar.title}>
                  <div className="mb-1 flex items-center justify-between text-xs">
                    <span className="text-foreground/80">{bar.title}</span>
                    <span className="font-mono tabular-nums text-muted-foreground">
                      {bar.percent}%
                    </span>
                  </div>
                  <div className="h-1.5 overflow-hidden rounded-full bg-border/50">
                    <div
                      className={cn("h-full rounded-full", bar.barClass)}
                      style={{ width: `${bar.percent}%` }}
                    />
                  </div>
                </div>
              ))}
            </div>
            <div className="mt-4 rounded-lg border border-border/60 bg-background/60 p-3">
              <div className="flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
                <Icons.comment className="size-3 text-primary" />
                Фидбек по ответу
              </div>
              <p className="mt-1.5 text-xs leading-relaxed text-foreground/75">
                «Хорошо раскрыта суть async/await; стоит подтянуть детали SynchronizationContext…»
              </p>
            </div>
          </div>
        </div>
      </section>

      {/* Финальный CTA — спокойная панель в палитре платформы */}
      <section className="lt-reveal mx-auto w-full max-w-4xl px-4 pb-20">
        <div className="relative overflow-hidden rounded-3xl border border-border/70 bg-card px-6 py-12 text-center">
          <div
            aria-hidden
            className="pointer-events-none absolute -top-28 left-1/2 size-72 -translate-x-1/2 rounded-full bg-primary/[0.08] blur-3xl"
          />

          <h2 className="text-balance text-3xl font-bold tracking-tight sm:text-4xl">
            Готов узнать свой уровень?
          </h2>
          <p className="mx-auto mt-3 max-w-md text-sm text-muted-foreground">
            {test.totalQuestions} вопросов, ~20 минут — уровень, слабые места и план роста.
          </p>
          <Button size="lg" onClick={onStart} className="mt-7 h-12 px-8 text-base">
            {ctaLabel}
            <Icons.arrowRight className="size-4" />
          </Button>
          <p className="mt-4 font-mono text-xs text-muted-foreground/70">
            <span>{"//"}</span> вернуться можно в любой момент — прогресс не сгорает
          </p>
        </div>
      </section>
    </div>
  );
}
