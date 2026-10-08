import { Button } from "@/shared/ui/kit/button";
import { AlertCircle } from "lucide-react";

type Props = {
  error?: string;
  onRetry: () => void;
};

export function VideoErrorState({ error, onRetry }: Props) {
  return (
    <div className="border border-destructive/50 bg-destructive/5 rounded-lg p-4">
      <div className="flex items-start gap-3">
        <AlertCircle className="w-5 h-5 text-destructive shrink-0 mt-0.5" />
        <div className="flex-1">
          <p className="text-sm font-medium text-destructive">
            Ошибка загрузки
          </p>
          <p className="text-xs text-muted-foreground mt-1">
            {error || "Не удалось загрузить видео. Попробуйте ещё раз."}
          </p>
          <Button
            variant="outline"
            size="sm"
            className="mt-3"
            onClick={onRetry}
          >
            Попробовать снова
          </Button>
        </div>
      </div>
    </div>
  );
}
