"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { type Role } from "./roles";
import { useRoles } from "./use-roles";

interface RequireRoleProps {
  children: React.ReactNode;
  /** Minimum role required (hierarchical check via isAtLeast) */
  atLeast: Role;
  /** Where to redirect if access denied (default: /catalog) */
  redirectTo?: string;
}

/**
 * Client-side role guard (defense in depth — middleware is the primary check).
 * Renders children only if the user has the required role level.
 */
export function RequireRole({
  children,
  atLeast,
  redirectTo = "/catalog",
}: RequireRoleProps) {
  const { isAtLeast, isAuthenticated } = useRoles();
  const router = useRouter();
  const hasAccess = isAtLeast(atLeast);

  useEffect(() => {
    if (isAuthenticated && !hasAccess) {
      router.replace(redirectTo);
    }
  }, [isAuthenticated, hasAccess, redirectTo, router]);

  if (!isAuthenticated || !hasAccess) return null;

  return <>{children}</>;
}
