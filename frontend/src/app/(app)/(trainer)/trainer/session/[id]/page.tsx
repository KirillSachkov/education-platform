import type { Metadata } from "next";
import { Suspense } from "react";
import { DrillSessionPage } from "@/widgets/drill-session-runner";

interface Props {
  params: Promise<{ id: string }>;
}

export const metadata: Metadata = {
  title: "Тренировка",
  description: "Прохождение тренировки тренажёра.",
};

/**
 * `/trainer/session/{id}` (#568) — прохождение DRILL-сессии (IN_PROGRESS → loop
 * с мгновенной проверкой) или разбор завершённой (COMPLETED → review). Сессия
 * scoped по UserId — клиентский загрузчик разруливает 404/403.
 */
export default async function TrainerSessionPage({ params }: Props) {
  const { id } = await params;
  return (
    <div className="px-3 py-4 md:p-6">
      {/* Suspense обязателен — загрузчик читает useSearchParams (#656). */}
      <Suspense fallback={null}>
        <DrillSessionPage sessionId={id} />
      </Suspense>
    </div>
  );
}
