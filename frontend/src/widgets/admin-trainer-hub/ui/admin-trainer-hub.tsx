"use client";

import { AdminTrainerStatsPage } from "@/features/admin-trainer-stats";
import { MockInterviewsManager } from "@/features/mock-interview-builder";
import { TrainerContentManager } from "@/features/trainer-content-admin";
import { TrainerSubscriptionManager } from "@/features/trainer-subscription";
import { useSearchParams } from "next/navigation";

const TABS = ["content", "mock", "subscription", "stats"] as const;
type HubTab = (typeof TABS)[number];

const DEFAULT_TAB: HubTab = "content";

function parseTab(value: string | null): HubTab {
  return TABS.includes(value as HubTab) ? (value as HubTab) : DEFAULT_TAB;
}

/**
 * Админ-раздел тренажёра `/trainer/admin` (#623). Навигацию между разделами
 * (Контент · Подписка · Статистика) ведёт `TrainerSidebar` через `?tab=` —
 * собственного in-page таб-бара здесь больше нет (был дублем сайдбара). Каждый
 * под-менеджер рендерит свой заголовок, поэтому общей шапки у хаба тоже нет.
 */
export function AdminTrainerHub() {
  const searchParams = useSearchParams();
  const tab = parseTab(searchParams.get("tab"));

  return (
    <div className="mx-auto w-full max-w-[1500px] space-y-5 p-4 md:p-6">
      {tab === "content" && <TrainerContentManager />}
      {tab === "mock" && <MockInterviewsManager />}
      {tab === "subscription" && <TrainerSubscriptionManager />}
      {tab === "stats" && <AdminTrainerStatsPage />}
    </div>
  );
}
