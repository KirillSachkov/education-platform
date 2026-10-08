import { type EnvelopeError, type ErrorType, isEnvelopeError } from "@/shared/api/errors";
import { cn } from "@/shared/lib/css";
import { AlertCircle, RefreshCw } from "lucide-react";
import { Button } from "./button";

type ErrorCardProps = {
  error: Error | EnvelopeError | null;
  className?: string;
  onRetry?: () => void;
};

const errorTypeLabels: Record<ErrorType, string> = {
  VALIDATION: "Ошибка валидации",
  NOT_FOUND: "Не найдено",
  FAILURE: "Произошла ошибка",
  CONFLICT: "Конфликт данных",
  AUTHENTICATION: "Требуется авторизация",
  AUTHORIZATION: "Доступ запрещён",
};

export function ErrorCard({ error, className, onRetry }: ErrorCardProps) {
  if (!error) return null;

  const isEnvelope = isEnvelopeError(error);
  const title = isEnvelope ? errorTypeLabels[error.type] : "Произошла ошибка";
  const message = isEnvelope ? error.firstMessage : error.message;

  return (
    <div
      className={cn(
        "flex min-h-[200px] flex-col items-center justify-center gap-4 text-center",
        className
      )}
    >
      <AlertCircle className="h-10 w-10 text-muted-foreground/50" />
      <div className="space-y-1">
        <p className="font-medium text-foreground">{title}</p>
        <p className="text-sm text-muted-foreground">{message}</p>
      </div>
      {onRetry && (
        <Button variant="outline" size="sm" onClick={onRetry}>
          <RefreshCw className="mr-2 h-4 w-4" />
          Попробовать снова
        </Button>
      )}
    </div>
  );
}
