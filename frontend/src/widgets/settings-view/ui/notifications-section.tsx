"use client";

import { NotificationPreferencesSection } from "@/features/profile-manage";
import { PushDeviceToggle } from "@/features/web-push";

/**
 * Раздел «Уведомления» — per-device push (web-push, #342) + preferences (каналы × типы).
 * Push-тумблер живёт в features/web-push (browser PushManager), форма каналов — в
 * profile-manage. Widget композитит обе фичи (cross-feature import допустим на widget-уровне).
 */
export function NotificationsPreferencesSection() {
  return (
    <section className="rounded-2xl border border-border/50 bg-card/40 p-5 sm:p-6 space-y-4">
      <PushDeviceToggle />
      <NotificationPreferencesSection />
    </section>
  );
}
