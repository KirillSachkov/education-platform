import { OnboardingProviders } from "./providers";

/**
 *  Layout для /onboarding/*. Не находится под (app), поэтому не получает
 *  AppProviders → нужен свой QueryClientProvider + SessionProvider.
 *  Используется для /onboarding/profile (display-name capture перед входом
 *  в платформу). Plan-onboarding теперь рендерится модалкой поверх (app),
 *  отдельного route у него нет.
 */
export default function OnboardingLayout({ children }: { children: React.ReactNode }) {
  return <OnboardingProviders>{children}</OnboardingProviders>;
}
