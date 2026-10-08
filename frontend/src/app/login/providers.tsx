"use client";

import { getQueryClient } from "@/shared/api/query-client";
import { QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "@/shared/ui/kit/sonner";

export function LoginProviders({ children }: { children: React.ReactNode }) {
  const queryClient = getQueryClient();

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Toaster position="top-center" duration={3000} richColors closeButton />
    </QueryClientProvider>
  );
}
