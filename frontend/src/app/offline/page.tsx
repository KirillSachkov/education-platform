import type { Metadata } from "next";
import { LogoMark } from "@/shared/ui/kit/logo";
import { OfflineRetryButton } from "./offline-retry-button";

export const metadata: Metadata = {
  title: "Нет соединения",
  robots: { index: false, follow: false },
};

export default function OfflinePage() {
  return (
    <div
      className="flex min-h-svh items-center justify-center bg-background px-6"
      style={{
        paddingTop: "max(env(safe-area-inset-top), 1.5rem)",
        paddingBottom: "max(env(safe-area-inset-bottom), 1.5rem)",
        paddingLeft: "max(env(safe-area-inset-left), 1.5rem)",
        paddingRight: "max(env(safe-area-inset-right), 1.5rem)",
      }}
    >
      <div className="mx-auto flex w-full max-w-sm flex-col items-center gap-6 text-center">
        <LogoMark size={48} className="text-primary" />
        <div className="space-y-2">
          <h1 className="text-2xl font-extrabold tracking-tight font-[family-name:var(--font-sora)]">
            Нет соединения
          </h1>
          <p className="text-sm text-muted-foreground">
            Проверьте интернет и попробуйте снова. Некоторые ранее открытые страницы могут быть
            доступны из кэша.
          </p>
        </div>
        <OfflineRetryButton />
      </div>
    </div>
  );
}
