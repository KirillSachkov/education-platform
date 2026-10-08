import type { Metadata } from "next";
import { AdminNotificationsPage } from "@/features/admin-notifications";

export const metadata: Metadata = {
  title: "Журнал уведомлений",
};

export default function AdminNotificationsRoute() {
  return <AdminNotificationsPage />;
}
