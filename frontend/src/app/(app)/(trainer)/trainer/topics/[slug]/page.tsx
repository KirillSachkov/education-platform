import type { Metadata } from "next";
import { TrainerTopicStart } from "@/features/start-drill-session";

interface Props {
  params: Promise<{ slug: string }>;
}

export const metadata: Metadata = {
  title: "Тренировка по теме",
  description: "Набор вопросов по теме с мгновенной проверкой и разбором.",
};

/**
 * `/trainer/topics/{slug}` (#568) — страница темы: метаданные + старт DRILL.
 * Тему резолвит клиентский компонент по slug из общего списка тем.
 */
export default async function TrainerTopicPage({ params }: Props) {
  const { slug } = await params;
  return (
    <div className="p-4 md:p-6">
      <TrainerTopicStart slug={slug} />
    </div>
  );
}
