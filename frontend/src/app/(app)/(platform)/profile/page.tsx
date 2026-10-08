import type { Metadata } from "next";
import { Suspense } from "react";
import { Loader2 } from "lucide-react";
import { ProfileView } from "@/widgets/profile-view";
import { MobileAccountMenu } from "@/widgets/mobile-account-menu";
import { InstallPromptButton } from "@/widgets/pwa-install-prompt";

export const metadata: Metadata = {
  title: "Профиль",
};

export default function ProfileRoute() {
  return (
    <Suspense
      fallback={
        <div className="flex items-center justify-center min-h-[60vh]">
          <Loader2 className="size-6 animate-spin text-muted-foreground" />
        </div>
      }
    >
      <ProfileView />
      {/* Mobile-only install CTA. Self-hides when the browser hasn't fired
          beforeinstallprompt (iOS Safari, already installed, etc.) — the
          component returns null. Placed above the rest of the account menu
          because installing is a one-time action that becomes invisible
          afterwards. */}
      <div className="md:hidden mx-auto w-full max-w-3xl px-4">
        <InstallPromptButton />
      </div>
      <MobileAccountMenu />
    </Suspense>
  );
}
