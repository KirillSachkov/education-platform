import type { Metadata } from "next";
import { SecuritySection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Безопасность · Настройки",
};

export default function SettingsSecurityPage() {
  return <SecuritySection />;
}
