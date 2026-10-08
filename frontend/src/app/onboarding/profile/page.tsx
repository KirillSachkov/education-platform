import { ProfileOnboardingForm } from "@/features/onboarding-profile";
import { auth } from "@/shared/auth/auth";
import { LogoMark } from "@/shared/ui/kit/logo";
import type { Metadata } from "next";
import { redirect } from "next/navigation";

export const metadata: Metadata = {
  title: "Завершите профиль",
};

export default async function OnboardingProfilePage() {
  const session = await auth();

  if (!session) {
    redirect("/login");
  }

  // Page is voluntary after #51 (no auto-redirect from proxy.ts).
  // Empty displayName = user has no name yet → keep them here so they can optionally fill it.
  // Non-empty (already named) or undefined (legacy session) → not the target audience for this page.
  if (session.user.displayName !== "") {
    redirect("/");
  }

  return (
    <div className="flex min-h-svh bg-background relative overflow-hidden">
      <div className="pointer-events-none absolute inset-0">
        <div className="absolute top-[10%] left-[15%] w-[400px] h-[400px] rounded-full bg-primary/4 blur-3xl" />
        <div className="absolute bottom-[10%] right-[10%] w-[350px] h-[350px] rounded-full bg-cyan/3 blur-3xl" />
      </div>

      <div className="relative m-auto flex w-full max-w-sm flex-col items-center gap-8 px-4">
        <div className="flex flex-col items-center gap-4">
          <LogoMark size={48} className="text-primary" />
          <p className="text-sm font-bold font-[family-name:var(--font-sora)]">
            Sachkov<span className="text-primary">Learn</span>
          </p>
        </div>

        <div
          role="status"
          aria-live="polite"
          className="flex items-center gap-2 text-xs text-muted-foreground"
        >
          <span className="inline-flex h-1.5 w-12 overflow-hidden rounded-full bg-muted">
            <span className="block w-full bg-primary" />
          </span>
          Последний шаг — один клик до начала
        </div>

        <div className="w-full rounded-2xl border border-border/60 bg-card p-6 shadow-sm">
          <ProfileOnboardingForm username={session.user.username ?? ""} />
        </div>
      </div>
    </div>
  );
}
