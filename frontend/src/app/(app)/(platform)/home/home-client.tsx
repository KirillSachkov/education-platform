"use client";

import { HomePinsSection } from "@/features/home-pins";
import { AuthenticatedHome } from "@/features/student-learning";
import { TelegramLinkBanner } from "@/features/telegram-link";
import { PushPermissionPrompt } from "@/widgets/push-permission-prompt";

export function HomeClient() {
  return (
    <div className="mx-auto max-w-5xl px-4 sm:px-6 py-6 sm:py-10 space-y-6 sm:space-y-8">
      <AuthenticatedHome />
      <HomePinsSection />
      <TelegramLinkBanner />
      <PushPermissionPrompt />
    </div>
  );
}
