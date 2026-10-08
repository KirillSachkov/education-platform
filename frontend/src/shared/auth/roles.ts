/**
 * Роли платформы — зеркало Identity roles.
 * Используются на фронтенде для условного рендера UI.
 * Авторизация на бэкенде работает через permissions (маппинг groups → permissions).
 */
export const ROLES = {
  PARTICIPANT: "platform-participant",
  AUTHOR: "platform-author",
  EDITOR: "platform-editor",
  MODERATOR: "platform-moderator",
  ADMIN: "platform-admin",
  OWNER: "platform-owner",
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

/**
 * Иерархия ролей — от наименьших привилегий к наибольшим.
 * Используется в `isAtLeast()` для проверки "эта роль или выше".
 */
export const ROLE_HIERARCHY: readonly Role[] = [
  ROLES.PARTICIPANT,
  ROLES.AUTHOR,
  ROLES.EDITOR,
  ROLES.MODERATOR,
  ROLES.ADMIN,
  ROLES.OWNER,
];
