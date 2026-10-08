import type { Metadata } from "next";
import { Suspense } from "react";
import { IntegrationsSection } from "@/widgets/settings-view";

export const metadata: Metadata = {
  title: "Интеграции · Настройки",
};

export default function SettingsIntegrationsPage() {
  return (
    <Suspense fallback={null}>
      <IntegrationsSection />
    </Suspense>
  );
}
