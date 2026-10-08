import type { Metadata } from "next";
import { AppearanceSection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Внешний вид · Настройки",
};

export default function SettingsAppearancePage() {
  return <AppearanceSection />;
}
