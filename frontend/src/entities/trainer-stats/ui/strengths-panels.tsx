"use client";

import Link from "next/link";

import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";

import type { TrainerStrengthSampleSize, TrainerStrengthTopic } from "../types";

/**
 * Единый блок «Сильные и слабые стороны» по ИЗМЕРЕННОМУ mastery (#614 H/H2). Один источник
 * правды для обеих поверхностей тренажёра — вкладки «Статистика» (дашборд) и «Симуляция»
 * (история моков). Тема попадает в колонки только пройдя бэк-порог попыток; под колонками —
 * строка объёма выборки, ниже — вторичная де-шумленная подсказка из AI-разбора моков.
 * Минималистичный вид: две колонки чипов с маленькой точкой-сигналом (зелёный/янтарный),
 * без тонированных боксов и иконок. Презентационный (данные тянет caller), живёт в entity-слое.
 */
export function TrainerStrengthsPanels({
  strong,
  weak,
  sample,
  mockHints,
}: {
  strong: TrainerStrengthTopic[];
  weak: TrainerStrengthTopic[];
  sample: TrainerStrengthSampleSize | undefined;
  mockHints: string[];
}) {
  const assessed = sample?.assessedTopics ?? 0;
  const hasColumns = strong.length > 0 || weak.length > 0;

  return (
    <div className="space-y-3">
      {hasColumns ? (
        <>
          <div className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
            {strong.length > 0 && <TopicColumn title="Сильные темы" topics={strong} tone="good" />}
            {weak.length > 0 && <TopicColumn title="Подтянуть" topics={weak} tone="warn" />}
          </div>
          {sample && assessed > 0 && <SampleSizeLine sample={sample} />}
        </>
      ) : (
        <p className="rounded-xl border border-dashed border-border/60 bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          Пройди несколько тренировок и мок-интервью — и здесь появится оценка сильных и слабых тем.
        </p>
      )}

      {mockHints.length > 0 && (
        <div className="space-y-1.5 pt-1">
          <p className="text-xs font-medium text-muted-foreground">Отмечено в мок-интервью</p>
          <div className="flex flex-wrap gap-2">
            {mockHints.map((topic) => (
              <span
                key={topic}
                className="rounded-full bg-muted px-3 py-1 text-xs font-medium text-muted-foreground"
              >
                {topic}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

/** Колонка «сильные»/«подтянуть»: тихий заголовок + облако чипов-тем. */
function TopicColumn({
  title,
  topics,
  tone,
}: {
  title: string;
  topics: TrainerStrengthTopic[];
  tone: "good" | "warn";
}) {
  return (
    <section className="space-y-2">
      <p className="text-xs font-medium text-muted-foreground">{title}</p>
      <TopicChips topics={topics} tone={tone} />
    </section>
  );
}

/** Строка контекста выборки: «оценка по N ответам в M сессиях · из них K мок-собесов». */
function SampleSizeLine({ sample }: { sample: TrainerStrengthSampleSize }) {
  return (
    <p className="text-xs text-muted-foreground">
      оценка по{" "}
      <span className="font-medium text-foreground/80 tabular-nums">{sample.totalAnswers}</span>{" "}
      {pluralize(sample.totalAnswers, "ответу", "ответам", "ответам")} в{" "}
      <span className="font-medium text-foreground/80 tabular-nums">{sample.sessionsCount}</span>{" "}
      {pluralize(sample.sessionsCount, "сессии", "сессиях", "сессиях")}
      {sample.mockCount > 0 && (
        <>
          {" · из них "}
          <span className="font-medium text-foreground/80 tabular-nums">{sample.mockCount}</span>{" "}
          {pluralize(sample.mockCount, "мок-собес", "мок-собеса", "мок-собесов")}
        </>
      )}
    </p>
  );
}

/**
 * Облако кликабельных чипов-тем (точка-сигнал + название + mastery%) в нейтральном тоне.
 * Цвет несёт только маленькая точка (зелёная — сильное, янтарная — зона роста), сам чип
 * нейтральный — чтобы блок не кричал. Чип ведёт на изучение темы (таб «Обучение» со scope).
 */
function TopicChips({ topics, tone }: { topics: TrainerStrengthTopic[]; tone: "good" | "warn" }) {
  const dot = tone === "good" ? "bg-green/70" : "bg-amber-500/70";
  return (
    <div className="flex flex-wrap gap-1.5">
      {topics.map((topic) => (
        <Link
          key={topic.topicId}
          href={routes.trainerStudyTopic(topic.topicId)}
          className="inline-flex max-w-full items-center gap-1.5 rounded-full border border-border/60 bg-card/50 px-2.5 py-1 text-xs text-foreground/85 transition-colors hover:bg-accent/30 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1 focus-visible:outline-none"
          title={`Изучать: ${topic.title}`}
        >
          <span className={cn("size-1.5 shrink-0 rounded-full", dot)} aria-hidden />
          <span className="truncate">{topic.title}</span>
          <span className="font-medium tabular-nums text-muted-foreground">
            {topic.masteryPercent}%
          </span>
        </Link>
      ))}
    </div>
  );
}
