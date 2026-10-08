const numberFormatter = new Intl.NumberFormat("ru");

type StatLineProps = {
  label: string;
  value: number | undefined;
  isLoading: boolean;
  /** Optional descriptive caption rendered as small grey text on the right under the value. */
  hint?: string;
};

export function StatLine({ label, value, isLoading, hint }: StatLineProps) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-muted-foreground">{label}</span>
      <div className="flex flex-col items-end">
        {isLoading || value === undefined ? (
          <span className="h-4 w-16 animate-pulse rounded bg-muted/40" aria-hidden />
        ) : (
          <span className="font-medium tabular-nums">{numberFormatter.format(value)}</span>
        )}
        {hint ? <span className="text-[10px] uppercase text-muted-foreground/70">{hint}</span> : null}
      </div>
    </div>
  );
}
