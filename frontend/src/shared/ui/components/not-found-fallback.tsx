import Link from "next/link";

interface NotFoundFallbackProps {
  message?: string;
  backHref: string;
  backLabel?: string;
}

export function NotFoundFallback({
  message = "Не найдено",
  backHref,
  backLabel = "Назад",
}: NotFoundFallbackProps) {
  return (
    <div className="flex flex-col items-center justify-center h-full p-6">
      <p className="text-muted-foreground mb-3">{message}</p>
      <Link href={backHref} className="text-primary text-sm">
        &larr; {backLabel}
      </Link>
    </div>
  );
}
