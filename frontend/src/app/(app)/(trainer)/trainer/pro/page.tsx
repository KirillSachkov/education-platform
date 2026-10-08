import type { Metadata } from "next";
import { TrainerProLanding } from "@/widgets/trainer-pro";

export const metadata: Metadata = {
  title: "Тренажёр Pro — подписка",
  description:
    "Подписка на тренажёр собеседований: голосовые ответы, мок-интервью, развёрнутые ответы с AI-разбором, умные повторы и все банки вопросов.",
};

/**
 * `/trainer/pro` (#623) — лендинг подписки тренажёра в его же пространстве
 * (route-группа `(trainer)` → фиолетовая тема + `TrainerSidebar` из layout'а).
 */
export default function TrainerProPage() {
  return <TrainerProLanding />;
}
