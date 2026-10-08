"use client";

import { useSession } from "next-auth/react";

export function useIsAuthenticated() {
  const { status } = useSession();
  return status === "authenticated";
}
