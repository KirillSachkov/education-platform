"use client";

import { useSession } from "next-auth/react";
import { useEffect } from "react";
import { localLogoutToLogin } from "./full-logout";

export function SessionGuard({ children }: { children: React.ReactNode }) {
  const { data: session } = useSession();

  useEffect(() => {
    if (session?.error === "RefreshTokenError") {
      // Dead session — clear locally and hard-redirect to /login. NOT the
      // federated end_session flow: with a stale id_token it bounces back to an
      // authenticated page and the page reloads forever.
      void localLogoutToLogin();
    }
  }, [session?.error]);

  return <>{children}</>;
}
