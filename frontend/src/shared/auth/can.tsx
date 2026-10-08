"use client";

import type { Role } from "./roles";
import { useRoles } from "./use-roles";

interface CanProps {
  children: React.ReactNode;

  /** Показать children если у пользователя есть эта роль. */
  role?: Role;

  /** Показать children если у пользователя есть хотя бы одна из ролей. */
  roles?: Role[];

  /** Показать children если роль пользователя не ниже указанной. */
  atLeast?: Role;

  /** Что показать если доступа нет (по умолчанию — ничего). */
  fallback?: React.ReactNode;
}

/**
 * Компонент условного рендера по роли пользователя.
 *
 * @example
 * <Can role="platform-admin">
 *   <DeleteButton />
 * </Can>
 *
 * <Can atLeast="platform-author">
 *   <CreateModuleButton />
 * </Can>
 *
 * <Can roles={["platform-moderator", "platform-admin"]} fallback={<p>Нет доступа</p>}>
 *   <AdminPanel />
 * </Can>
 */
export function Can({
  children,
  role,
  roles,
  atLeast,
  fallback = null,
}: CanProps) {
  const { hasRole, hasAnyRole, isAtLeast } = useRoles();

  let hasAccess = false;

  if (role) {
    hasAccess = hasRole(role);
  } else if (roles) {
    hasAccess = hasAnyRole(roles);
  } else if (atLeast) {
    hasAccess = isAtLeast(atLeast);
  }

  return hasAccess ? <>{children}</> : <>{fallback}</>;
}
