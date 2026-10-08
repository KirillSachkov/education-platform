const DISMISS_KEY_PREFIX = "access-expired-dismissed:";

/**
 * Сворачивание модалки «срок истёк» запоминается НАВСЕГДА (localStorage) per grantId:
 * это разовое уведомление-подтверждение, а не повторяющийся onboarding-гейт. Один раз
 * увидел/закрыл — больше по этому истёкшему доступу не всплывает. Новый истёкший grant
 * (другой grantId) покажет модалку заново.
 */
export function readExpiredDismissed(grantId: string): boolean {
  try {
    return localStorage.getItem(DISMISS_KEY_PREFIX + grantId) === "1";
  } catch {
    return false;
  }
}

export function rememberExpiredDismissed(grantId: string): void {
  try {
    localStorage.setItem(DISMISS_KEY_PREFIX + grantId, "1");
  } catch {
    // localStorage недоступен (private mode и т.п.) — не критично: модалка просто
    // покажется снова на следующем рендере.
  }
}
