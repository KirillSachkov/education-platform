export function BillingSettingsCard() {
  return (
    <section className="rounded-lg border border-border/40 bg-card/40 p-4">
      <h2 className="text-sm font-semibold">Продажи закрыты</h2>
      <p className="mt-1 max-w-prose text-xs text-muted-foreground">
        Новые покупки и автоматические списания отключены. История оплат, возвраты и управление
        существующими доступами доступны в рабочей области.
      </p>
    </section>
  );
}
