import { RequireDisplayNameGate } from "@/features/onboarding-profile";
import { AppProviders } from "@/shared/providers/app-providers";
import { AccessExpiredOverlay } from "@/widgets/access-expired-overlay";
import { AiJobsTracker } from "@/widgets/ai-jobs-tracker";
import { OnboardingOverlay } from "@/widgets/onboarding-overlay";

export default function AppRouteLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <AppProviders>
      {/* Skip-to-content link — first focusable element for keyboard users.
          Visually hidden until focused, then slides into view. */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-[100] focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-primary-foreground focus:shadow-lg focus:outline-none focus:ring-2 focus:ring-ring/60"
      >
        Перейти к контенту
      </a>
      {children}
      <OnboardingOverlay />
      <AccessExpiredOverlay />
      <RequireDisplayNameGate />
      <AiJobsTracker />
    </AppProviders>
  );
}
