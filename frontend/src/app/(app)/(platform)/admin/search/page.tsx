import type { Metadata } from "next";
import { AdminSearchPage } from "@/features/admin-search";

export const metadata: Metadata = {
  title: "Полнотекстовый поиск",
};

export default function AdminSearchRoute() {
  return <AdminSearchPage />;
}
