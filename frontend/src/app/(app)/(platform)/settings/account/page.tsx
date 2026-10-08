import type { Metadata } from "next";
import { AccountSection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Аккаунт · Настройки",
};

export default function SettingsAccountPage() {
  return <AccountSection />;
}
