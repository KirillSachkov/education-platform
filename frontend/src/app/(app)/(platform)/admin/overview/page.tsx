import type { Metadata } from "next";
import { AdminOverviewPage } from "@/features/admin-overview";

export const metadata: Metadata = {
  title: "Обзор админки",
};

export default function AdminOverviewRoute() {
  return <AdminOverviewPage />;
}
