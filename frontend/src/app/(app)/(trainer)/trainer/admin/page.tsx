"use client";

import { AdminTrainerHub } from "@/widgets/admin-trainer-hub";
import { ROLES, RequireRole } from "@/shared/auth";
import { Suspense } from "react";

/**
 * Админка тренажёра в пространстве самого тренажёра (#623): `/trainer/admin` под
 * route-группой `(trainer)` → наследует фиолетовую тему + `TrainerSidebar`.
 * Гейт на ADMIN (как `/admin/*`). Хаб читает `?tab=` → Suspense. Старый
 * `/admin/trainer` редиректит сюда (см. `routes.ts` / `next.config.ts`).
 */
export default function TrainerAdminRoute() {
  return (
    <RequireRole atLeast={ROLES.ADMIN}>
      <Suspense>
        <AdminTrainerHub />
      </Suspense>
    </RequireRole>
  );
}
