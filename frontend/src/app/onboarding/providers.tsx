"use client";

import { getQueryClient } from "@/shared/api/query-client";
import { SessionGuard } from "@/shared/auth/session-guard";
import { TokenSync } from "@/shared/auth/token-sync";
import { QueryClientProvider } from "@tanstack/react-query";
import { SessionProvider } from "next-auth/react";
import { Toaster } from "@/shared/ui/kit/sonner";

export function OnboardingProviders({ children }: { children: React.ReactNode }) {
  const queryClient = getQueryClient();

  return (
    <SessionProvider
      refetchInterval={4 * 60}
      refetchOnWindowFocus={false}
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
