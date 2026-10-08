import { cn } from "@/shared/lib/css";
import { Skeleton } from "@/shared/ui/kit/skeleton";

interface LockedContentPlaceholderProps {
  /** Сколько строк-скелетонов под стем (по умолчанию 3). */
  lines?: number;
  /** Сколько строк-скелетонов под варианты ответа (0 — без них). */
  options?: number;
  className?: string;
}

/**
 * Размытый скелетон-плейсхолдер для редактированного сервером контента тренажёра (#674).
 *
 * Сервер уже вырезал текст заблокированного вопроса (`questionText`/`stem === null`,
 * `options: []`) — здесь НЕЧЕМУ утечь, это чисто декоративные полосы. Лёгкий `blur` + нижний
 * `mask`-градиент намекают, что за замком есть скрытый контент (техника
 * `modern-web-guidance:soft-edge-content-fade`), без semi-transparent оверлея.
 * `aria-hidden` + `select-none` + `pointer-events-none` — для скринридера/выделения невидим.
 */
export function LockedContentPlaceholder({
  lines = 3,
  options = 0,
  className,
}: LockedContentPlaceholderProps) {
  // Чуть разная ширина строк, чтобы было похоже на настоящий абзац.
  const lineWidths = ["w-[92%]", "w-full", "w-[78%]", "w-[85%]", "w-[60%]"];

  return (
    <div
      aria-hidden="true"
      className={cn(
        "pointer-events-none select-none space-y-2.5 blur-[2.5px]",
        // Нижний fade — «контент продолжается за замком» (mask, не оверлей).
        "[mask-image:linear-gradient(to_bottom,black_60%,transparent)] [-webkit-mask-image:linear-gradient(to_bottom,black_60%,transparent)]",
        className,
      )}
    >
      {Array.from({ length: Math.max(1, lines) }).map((_, index) => (
        <Skeleton
          key={`line-${index}`}
          className={cn("h-4 rounded", lineWidths[index % lineWidths.length])}
        />
      ))}
      {options > 0 && (
        <div className="space-y-2 pt-2">
          {Array.from({ length: options }).map((_, index) => (
            <Skeleton key={`opt-${index}`} className="h-10 w-full rounded-lg" />
          ))}
        </div>
      )}
    </div>
  );
}
