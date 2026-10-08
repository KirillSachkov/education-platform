import type { Metadata } from "next";
import { SubscriptionsSection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Подписки · Настройки",
};

export default function SettingsSubscriptionsPage() {
  return <SubscriptionsSection />;
}
