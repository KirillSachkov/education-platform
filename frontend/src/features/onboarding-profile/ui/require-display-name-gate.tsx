"use client";

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { useSession } from "next-auth/react";
import { ProfileOnboardingForm } from "./profile-onboarding-form";

/**
 * Не пускает авторизованного юзера дальше, пока у него не задан `display_name`.
 *
 * Триггер — claim `display_name === ""` (источник: `Authorization.cs` эмитит пустую
 * строку для `Account.DisplayName == null`). `undefined` означает legacy-сессию без
 * этого claim — модалку не показываем, на следующем refresh JWT обновится и поймает.
 *
 * Mounted в `(app)/layout.tsx` — анонимы и `/onboarding/profile` (вне `(app)`) не задеваются.
 */
export function RequireDisplayNameGate() {
  const { data: session, status } = useSession();
  const open = status === "authenticated" && session?.user.displayName === "";

  if (!open) return null;

  return (
    <Dialog open>
      <DialogContent
        showCloseButton={false}
        onEscapeKeyDown={(e) => e.preventDefault()}
        onPointerDownOutside={(e) => e.preventDefault()}
        onInteractOutside={(e) => e.preventDefault()}
        className="max-w-sm"
      >
        <DialogHeader className="sr-only">
          <DialogTitle>Завершите профиль</DialogTitle>
          <DialogDescription>
            Укажите отображаемое имя, чтобы продолжить пользоваться платформой.
          </DialogDescription>
        </DialogHeader>
        <ProfileOnboardingForm
          username={session.user.username ?? ""}
          redirectTo={null}
        />
      </DialogContent>
    </Dialog>
  );
}
