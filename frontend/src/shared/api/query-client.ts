import { QueryClient } from "@tanstack/react-query";
import { EnvelopeError, ForbiddenError } from "./errors";

export function makeQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        // 60s default — most read endpoints don't need fresher data.
        // Per-query staleTime overrides this where appropriate (e.g. 5min
        // for curriculum/landing/notifications, 30s for fast-moving progress).
        staleTime: 60_000,
        gcTime: 5 * 60 * 1000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) => {
          if (error instanceof EnvelopeError || error instanceof ForbiddenError) {
            return false;
          }
          if (
            typeof error === "object" &&
            error !== null &&
            "response" in error &&
            (error as { response?: { status?: number } }).response?.status === 401
          ) {
            return false;
          }
          return failureCount < 3;
        },
      },
      mutations: {
        retry: false,
      },
    },
  });
}

let browserQueryClient: QueryClient | undefined;

export function getQueryClient() {
  if (typeof window === "undefined") {
    // Server: always make a new query client to avoid cross-request data leakage
    return makeQueryClient();
  }
  // Browser: reuse the same query client across the app
  if (!browserQueryClient) browserQueryClient = makeQueryClient();
  return browserQueryClient;
}
