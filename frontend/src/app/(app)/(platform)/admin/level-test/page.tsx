import type { Metadata } from "next";
import { AdminLevelTestPage } from "@/features/admin-level-test";

export const metadata: Metadata = {
  title: "Тест уровня — аналитика",
};

export default function AdminLevelTestRoute() {
  return <AdminLevelTestPage />;
}
