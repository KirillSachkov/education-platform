"use client";

import { Icons } from "@/shared/ui/icons";

type DiagnosticsListProps = {
  diagnostics: string[];
};

/**
 * Diagnostics из post-purchase-status (#444) — backend-сформированные подсказки
 * («нет оплаченного заказа», «грант есть, но Telegram не привязан» и т.п.).
 * Рендерим как warning-строки; пустой список не показываем.
 */
export function DiagnosticsList({ diagnostics }: DiagnosticsListProps) {
  if (diagnostics.length === 0) return null;

  return (
    <ul className="space-y-1.5">
      {diagnostics.map((line) => (
        <li
          key={line}
          className="flex items-start gap-2 rounded-md border border-amber-500/30 bg-amber-500/5 px-3 py-2 text-xs text-amber-700 dark:text-amber-400"
        >
          <Icons.warning className="mt-0.5 size-3.5 shrink-0" />
          <span className="min-w-0 break-words">{line}</span>
        </li>
      ))}
    </ul>
  );
}
