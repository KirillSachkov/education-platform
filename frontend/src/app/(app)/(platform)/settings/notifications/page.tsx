import type { Metadata } from "next";
import { NotificationsPreferencesSection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Уведомления · Настройки",
};

export default function SettingsNotificationsPage() {
  return <NotificationsPreferencesSection />;
}
