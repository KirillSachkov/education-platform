"use client";

import { useSession } from "next-auth/react";
import { type Role, ROLE_HIERARCHY } from "./roles";

/**
 * Хук для работы с ролями текущего пользователя.
 *
 * @example
 * const { hasRole, isAtLeast } = useRoles();
 * if (hasRole("platform-admin")) { ... }
 * if (isAtLeast("platform-author")) { ... } // true для author, editor, moderator, admin, owner
 */
export function useRoles() {
  const { data: session, status } = useSession();

  const raw = session?.user?.roles;
  const roles = Array.isArray(raw) ? raw : raw ? [raw] : [];

  /**
   * Проверяет, есть ли у пользователя конкретная роль.
   */
  function hasRole(role: Role): boolean {
    return roles.includes(role);
  }

  /**
   * Проверяет, есть ли у пользователя хотя бы одна из указанных ролей.
   */
  function hasAnyRole(checkRoles: Role[]): boolean {
    return checkRoles.some((role) => roles.includes(role));
  }

  /**
   * Проверяет, что у пользователя роль не ниже указанной по иерархии.
   * Например, `isAtLeast("platform-author")` вернёт true для author и старших ролей.
   */
  function isAtLeast(minimumRole: Role): boolean {
    const minimumIndex = ROLE_HIERARCHY.indexOf(minimumRole);

    if (minimumIndex === -1) return false;

    return roles.some((r) => {
      const roleIndex = ROLE_HIERARCHY.indexOf(r as Role);
      return roleIndex >= minimumIndex;
    });
  }

  return {
    roles,
    isAuthenticated: !!session,
    isLoading: status === "loading",
    hasRole,
    hasAnyRole,
    isAtLeast,
  };
}
