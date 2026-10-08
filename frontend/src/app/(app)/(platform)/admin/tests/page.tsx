import type { Metadata } from "next";
import { AdminQuizStatsPage } from "@/features/admin-quiz-stats";

export const metadata: Metadata = {
  title: "Статистика тестов",
};

export default function AdminTestsRoute() {
  return <AdminQuizStatsPage />;
}
