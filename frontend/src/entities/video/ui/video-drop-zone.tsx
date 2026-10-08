import { cn } from "@/shared/lib/css";
import { Video } from "lucide-react";

type Props = {
  isDragging: boolean;
  onDragOver: (e: React.DragEvent) => void;
  onDragLeave: (e: React.DragEvent) => void;
  onDrop: (e: React.DragEvent) => void;
  onPaste: (e: React.ClipboardEvent) => void;
  onClick: () => void;
};

export function VideoDropZone({
  isDragging,
  onDragOver,
  onDragLeave,
  onDrop,
  onPaste,
  onClick,
}: Props) {
  const handleKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "Enter" && e.key !== " ") return;
    e.preventDefault();
    onClick();
  };

  return (
    <div
      className={cn(
        "border-2 border-dashed rounded-lg p-8 text-center transition-colors cursor-pointer",
        isDragging
          ? "border-primary bg-primary/5"
          : "border-muted-foreground/25 hover:border-primary/50",
      )}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onPaste={onPaste}
      onClick={onClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
      aria-label="Выберите, вставьте или перетащите видео"
    >
      <Video className="w-10 h-10 mx-auto mb-3 text-muted-foreground" />
      <p className="text-sm font-medium">Перетащите видео сюда</p>
      <p className="text-xs text-muted-foreground mt-1">MP4, WebM, MOV до 5 ГБ</p>
    </div>
  );
}
