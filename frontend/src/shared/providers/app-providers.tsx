"use client";

import { QueryClientProvider } from "@tanstack/react-query";
import { SessionProvider } from "next-auth/react";
import { SessionGuard } from "../auth/session-guard";
import { TokenSync } from "../auth/token-sync";
import { getQueryClient } from "../api/query-client";
import { Toaster } from "../ui/kit/sonner";

export function AppProviders({ children }: { children: React.ReactNode }) {
  const queryClient = getQueryClient();

  return (
    <SessionProvider
      // 4 мин < 5-мин access-token: jwt() callback всегда успевает рефрешнуть
      // до истечения, активный юзер не отправляет запросов со stale Bearer.
      // Параллельный /connect/token race больше не страшен — ротация выключена
      // на backend'е (см. #204, DisableRollingRefreshTokens).
      refetchInterval={4 * 60}
      // На возврате в таб подтягиваем свежий jwt() — иначе после долгой
      // неактивности UI остаётся в stale unauthenticated, пока юзер не нажмёт F5.
      refetchOnWindowFocus={true}
      refetchWhenOffline={false}
    >
      <QueryClientProvider client={queryClient}>
        <TokenSync />
        <SessionGuard>{children}</SessionGuard>
        <Toaster position="top-center" duration={3000} richColors closeButton />
      </QueryClientProvider>
    </SessionProvider>
  );
}
