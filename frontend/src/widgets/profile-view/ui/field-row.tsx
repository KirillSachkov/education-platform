import { ExternalLink } from "lucide-react";

export function FieldRow({
  label,
  value,
  icon,
  href,
}: {
  label: string;
  value: string | number | null | undefined;
  icon?: React.ReactNode;
  href?: string;
}) {
  if (!value && value !== 0) return null;

  return (
    <div className="flex items-start gap-3 py-2.5">
      {icon && (
        <span className="text-muted-foreground mt-0.5 shrink-0">{icon}</span>
      )}
      <div className="min-w-0">
        <p className="text-xs text-muted-foreground mb-0.5">{label}</p>
        {href ? (
          <a
            href={href}
            target="_blank"
            rel="noopener noreferrer"
            className="text-sm text-primary hover:underline inline-flex items-center gap-1.5 max-w-full"
          >
            <span className="truncate">{String(value)}</span>
            <ExternalLink size={12} className="shrink-0" />
          </a>
        ) : (
          <p className="text-sm text-foreground break-words">{String(value)}</p>
        )}
      </div>
    </div>
  );
}
