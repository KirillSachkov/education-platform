import { formatNumericDate } from "@/shared/lib/date";
import { Icons } from "@/shared/ui/icons";

interface TrialAccessBannerProps {
  /**
   * Дата окончания самого позднего активного пробного доступа (ISO-строка
   * `expiresAt`). Если активных trial-grant'ов несколько — берём максимальный
   * срок, баннер один на всех.
   */
  expiresAt: string;
}

export function TrialAccessBanner({ expiresAt }: TrialAccessBannerProps) {
  return (
    <div className="mb-6 overflow-hidden rounded-2xl border border-primary/30 bg-gradient-to-br from-primary/[0.07] via-card to-card p-4 shadow-[0_0_40px_-22px_rgba(107,173,165,0.35)] sm:p-5">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <span className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Icons.clock className="size-5" />
          </span>
          <div className="min-w-0">
            <p className="text-sm font-semibold leading-snug text-foreground sm:text-base">
              Доступ активен до {formatNumericDate(expiresAt)}
            </p>
            <p className="mt-1 text-sm leading-relaxed text-muted-foreground">
              Срок ранее выданного доступа сохранён. Новые покупки и продления закрыты.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
}
