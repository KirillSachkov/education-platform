import type { Metadata } from "next";
import { ProgressDashboard } from "@/widgets/progress-dashboard";

export const metadata: Metadata = {
  title: "Мой прогресс",
  description:
    "Стрик, график активности по дням и накопленный опыт — ваш учебный прогресс на платформе.",
};

/**
 * Глобальная страница «Мой прогресс»: стрик, GitHub-style сетка активности
 * за 12 недель, карточка уровня/XP и итоги за всё время. Данные —
 * GET /progress/my/activity/ (ProgressService). Аноним видит login-CTA
 * внутри дашборда (как на /saved).
 */
export default function ProgressPage() {
  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-4 md:p-6">
      <header className="space-y-2">
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">Мой прогресс</h1>
        <p className="text-sm text-muted-foreground sm:text-base">
          Стрик, активность по дням и накопленный опыт — всё обучение в одном месте.
        </p>
      </header>
      <ProgressDashboard />
    </div>
  );
}
