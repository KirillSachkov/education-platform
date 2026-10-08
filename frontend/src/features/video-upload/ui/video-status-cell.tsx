"use client";

import { cn } from "@/shared/lib/css";
import type { MediaStatus, VideoInfo } from "@/shared/types/media";
import { Progress } from "@/shared/ui/kit/progress";
import { AlertCircle, CheckCircle2, Loader2, Play, Upload } from "lucide-react";

export type VideoStatusCellProps = {
  video?: VideoInfo | null;
  uploadProgress?: number;
  isPolling?: boolean;
  onClick?: () => void;
  thumbnailUrl?: string | null;
};

export function VideoStatusCell({
  video,
  uploadProgress,
  isPolling = false,
  onClick,
  thumbnailUrl,
}: VideoStatusCellProps) {
  const status: MediaStatus | undefined = video?.status;
  const isClickable = !video || status !== "ready";

  if (status === "ready") {
    return (
      <VideoReadyCard thumbnailUrl={thumbnailUrl} duration={video?.duration} />
    );
  }

  return (
    <button
      type="button"
      className={cn("w-full transition-all", isClickable && "cursor-pointer")}
      onClick={isClickable ? onClick : undefined}
    >
      {!video && !isPolling && <VideoNone />}
      {!video && isPolling && <VideoAttaching />}
      {status === "uploading" && (
        <VideoUploading progress={uploadProgress ?? 0} />
      )}
      {(status === "pending_upload" || status === "uploaded" || !status) &&
        video && <VideoProcessingCard label="Ожидание обработки..." />}
      {status === "processing" && (
        <VideoProcessingCard label="Обработка видео..." />
      )}
      {status === "failed" && <VideoFailed />}
    </button>
  );
}

function VideoNone() {
  return (
    <div className="group border-2 border-dashed rounded-lg h-36 flex flex-col items-center justify-center text-center hover:border-muted-foreground/40 transition-colors">
      <div className="size-10 rounded-lg bg-muted/60 flex items-center justify-center mb-3 transition-colors group-hover:bg-muted">
        <Upload size={18} className="text-muted-foreground" />
      </div>
      <p className="text-sm text-muted-foreground transition-colors group-hover:text-foreground">
        Загрузить видео
      </p>
      <p className="text-xs text-muted-foreground/60 mt-1">
        Нажмите для выбора файла
      </p>
    </div>
  );
}

function VideoAttaching() {
  return (
    <div className="border rounded-lg h-36 flex items-center justify-center gap-3">
      <Loader2 size={18} className="animate-spin text-teal shrink-0" />
      <div className="text-left">
        <p className="text-sm font-medium">Прикрепление видео...</p>
        <p className="text-xs text-muted-foreground mt-0.5">
          Подождите немного
        </p>
      </div>
    </div>
  );
}

function VideoUploading({ progress }: { progress: number }) {
  return (
    <div className="border rounded-lg h-36 flex flex-col items-center justify-center px-8">
      <div className="w-full max-w-xs space-y-3">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Loader2 size={16} className="animate-spin text-teal" />
            <span className="text-sm font-medium">Загрузка видео</span>
          </div>
          <span className="text-sm tabular-nums font-medium text-muted-foreground">
            {progress}%
          </span>
        </div>
        <Progress value={progress} className="h-1.5" />
      </div>
    </div>
  );
}

function VideoProcessingCard({ label }: { label: string }) {
  return (
    <div className="border rounded-lg h-36 flex flex-col items-center justify-center gap-3 bg-muted/30">
      <div className="relative">
        <div className="size-16 rounded-lg bg-muted/80 flex items-center justify-center">
          <Play size={24} className="text-muted-foreground/50" />
        </div>
        <div className="absolute -bottom-1 -right-1 size-5 rounded-full bg-background border flex items-center justify-center">
          <Loader2 size={12} className="animate-spin text-teal" />
        </div>
      </div>
      <div className="text-center">
        <p className="text-sm font-medium">{label}</p>
        <p className="text-xs text-muted-foreground mt-0.5">
          Нажмите, чтобы прервать обработку
        </p>
      </div>
    </div>
  );
}

function VideoReadyCard({
  thumbnailUrl,
  duration,
}: {
  thumbnailUrl?: string | null;
  duration?: number | null;
}) {
  const formattedDuration = duration
    ? `${Math.floor(duration / 60)}:${String(Math.floor(duration % 60)).padStart(2, "0")}`
    : null;

  return (
    <div className="rounded-lg overflow-hidden border">
      <div className="relative aspect-video bg-muted/40">
        {thumbnailUrl ? (
          // eslint-disable-next-line @next/next/no-img-element -- Kinescope thumbnail URLs are signed and short-lived; next/image optimizer bypasses the signature TTL and caches stale URLs
          <img
            src={thumbnailUrl}
            alt=""
            className="absolute inset-0 w-full h-full object-cover"
          />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center">
            <Play size={32} className="text-muted-foreground/40" />
          </div>
        )}
        {formattedDuration && (
          <span className="absolute bottom-2 right-2 px-1.5 py-0.5 text-xs font-medium tabular-nums bg-black/70 text-white rounded">
            {formattedDuration}
          </span>
        )}
      </div>
      <div className="px-4 py-2.5 flex items-center gap-2 bg-green-50/50 dark:bg-green-950/20 border-t border-green-100 dark:border-green-900/30">
        <CheckCircle2 size={14} className="text-green-600 shrink-0" />
        <span className="text-xs font-medium text-green-700 dark:text-green-400">
          Видео загружено
        </span>
      </div>
    </div>
  );
}

function VideoFailed() {
  return (
    <div className="border border-destructive/30 bg-destructive/5 rounded-lg h-36 flex flex-col items-center justify-center gap-3">
      <div className="size-12 rounded-lg bg-destructive/10 flex items-center justify-center">
        <AlertCircle size={20} className="text-destructive" />
      </div>
      <div className="text-center">
        <p className="text-sm font-medium text-destructive">Ошибка обработки</p>
        <p className="text-xs text-muted-foreground mt-0.5">
          Нажмите, чтобы удалить и попробовать снова
        </p>
      </div>
    </div>
  );
}
