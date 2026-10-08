"use client";

import { useState } from "react";

import {
  TRAINER_ADMIN_STATS_RANGES,
  type TrainerAdminStatsRange,
} from "@/entities/trainer-admin-stats";
import { SegmentedControl } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";

import { AiFeedbackTab } from "./ai-feedback-tab";
import { FunnelTab } from "./funnel-tab";
import { MoneyTab } from "./money-tab";
import { QuestionQualityTab } from "./question-quality-tab";
import { TopicsTab } from "./topics-tab";
import { TrafficTab } from "./traffic-tab";
import { TrendsTab } from "./trends-tab";

const TABS = [
  { value: "money", label: "Деньги", Icon: Icons.creditCard },
  { value: "traffic", label: "Трафик", Icon: Icons.users },
  { value: "funnel", label: "Воронка", Icon: Icons.target },
  { value: "quality", label: "Качество", Icon: Icons.quiz },
  { value: "topics", label: "Темы и банки", Icon: Icons.library },
  { value: "trends", label: "Тренды", Icon: Icons.trending },
  { value: "ai-feedback", label: "Разбор ИИ", Icon: Icons.ai },
] as const;

/**
 * Админ-дашборд статистики тренажёра (#681/#680, вкладка `?tab=stats` хаба `/trainer/admin`).
 * Общий переключатель периода (7/30/90/365 дней) драйвит все запросы; разделы разнесены по
 * вкладкам (Деньги · Трафик · Воронка · Качество · Темы · Тренды) — каждая вкладка лениво
 * грузит свой запрос только когда активна (Radix размонтирует неактивный контент).
 */
export function AdminTrainerStatsPage() {
  const [days, setDays] = useState<TrainerAdminStatsRange>(30);
  const [tab, setTab] = useState<string>("money");

  return (
    <div className="space-y-5">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold">Статистика тренажёра</h2>
          <p className="text-sm text-muted-foreground">
            Деньги, трафик, воронка, качество вопросов, темы, динамика и оценки ИИ-разбора за
            выбранное окно.
          </p>
        </div>
        <SegmentedControl
          ariaLabel="Период статистики"
          value={String(days)}
          onChange={(v) => setDays(Number(v) as TrainerAdminStatsRange)}
          options={TRAINER_ADMIN_STATS_RANGES.map((r) => ({ value: String(r), label: `${r} дн.` }))}
        />
      </header>

      <Tabs value={tab} onValueChange={setTab}>
        <TabsList className="w-full">
          {TABS.map(({ value, label, Icon }) => (
            <TabsTrigger key={value} value={value}>
              <Icon className="size-4" />
              {label}
            </TabsTrigger>
          ))}
        </TabsList>

        <TabsContent value="money" className="mt-4">
          <MoneyTab days={days} />
        </TabsContent>
        <TabsContent value="traffic" className="mt-4">
          <TrafficTab days={days} />
        </TabsContent>
        <TabsContent value="funnel" className="mt-4">
          <FunnelTab days={days} />
        </TabsContent>
        <TabsContent value="quality" className="mt-4">
          <QuestionQualityTab days={days} />
        </TabsContent>
        <TabsContent value="topics" className="mt-4">
          <TopicsTab days={days} />
        </TabsContent>
        <TabsContent value="trends" className="mt-4">
          <TrendsTab days={days} />
        </TabsContent>
        <TabsContent value="ai-feedback" className="mt-4">
          <AiFeedbackTab days={days} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
