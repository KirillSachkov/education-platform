import type { Metadata } from "next";
import { Suspense } from "react";
import { TrainerHub } from "@/widgets/trainer-hub";

export const metadata: Metadata = {
  title: "Тренажёр",
  description:
    "Готовься к собеседованию на практике: выбери трек и тему, отвечай на вопросы с мгновенной проверкой и разбором, собери mock-интервью, следи за mastery по темам.",
};

/**
 * `/trainer` — единый хаб тренажёра (#568): верхний селектор трека + фильтр
 * направления + in-page вкладки (Тренировка / Mock-интервью / Прогресс /
 * Закладки). Composition: страница рендерит виджет, вся клиентская логика и
 * URL-состояние (`?track=&dir=&tab=`) — внутри него. Suspense обязателен —
 * хаб читает `useSearchParams`.
 */
export default function TrainerPage() {
  return (
    <Suspense fallback={null}>
      <TrainerHub />
    </Suspense>
  );
}
