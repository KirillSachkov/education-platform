const LAST_ORDER_STORAGE_KEY = "billing.lastOrderId";

/** Читает orderId из sessionStorage. Используется на /payment/success как fallback. */
export function readLastOrderId(): string | null {
  try {
    return sessionStorage.getItem(LAST_ORDER_STORAGE_KEY);
  } catch {
    return null;
  }
}

/** Очищает orderId из sessionStorage после терминального статуса. */
export function clearLastOrderId(): void {
  try {
    sessionStorage.removeItem(LAST_ORDER_STORAGE_KEY);
  } catch {
    // ignore
  }
}
