import { Button } from "@/shared/ui/kit/button";
import { Progress } from "@/shared/ui/kit/progress";
import { Video, X } from "lucide-react";
import { formatFileSize } from "../lib/validators";

type Props = {
  fileName?: string;
  fileSize?: number;
  progress: number;
  uploadedBytes?: number;
  totalBytes?: number;
  onCancel?: () => void;
};

export function VideoUploadingState({
  fileName,
  fileSize,
  progress,
  uploadedBytes,
  totalBytes,
  onCancel,
}: Props) {
  const progressText =
    uploadedBytes !== undefined && totalBytes
      ? `${formatFileSize(uploadedBytes)} / ${formatFileSize(totalBytes)}`
      : formatFileSize(fileSize ?? 0);

  return (
    <div className="min-w-0 w-full overflow-hidden rounded-lg border p-4 space-y-3">
      <div className="flex min-w-0 items-start gap-3">
        <div className="w-10 h-10 bg-primary/10 rounded-lg flex items-center justify-center shrink-0">
          <Video className="w-5 h-5 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium truncate">{fileName}</p>
          <p className="truncate text-xs text-muted-foreground">
            {progressText}
          </p>
        </div>
        {onCancel && (
          <Button
            variant="ghost"
            size="icon"
            className="h-8 w-8 shrink-0"
            onClick={onCancel}
          >
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>

      <div className="space-y-1.5">
        <Progress value={progress} className="h-2" />
        <div className="flex justify-between text-xs text-muted-foreground">
          <span>Загрузка...</span>
          <span>{progress}%</span>
        </div>
      </div>
    </div>
  );
}
