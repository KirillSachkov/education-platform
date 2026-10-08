const dateFormatter = new Intl.DateTimeFormat("ru", {
  dateStyle: "medium",
  timeStyle: "short",
});

/** Null-safe дата-таймстамп для админ-вкладки (#444). Совпадает с admin-user-detail. */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  return dateFormatter.format(d);
}

/** Сумма заказа в рублях из копеек. */
export function formatCents(cents: number, currency = "RUB"): string {
  return new Intl.NumberFormat("ru", {
    style: "currency",
    currency,
    maximumFractionDigits: 0,
  }).format(cents / 100);
}
