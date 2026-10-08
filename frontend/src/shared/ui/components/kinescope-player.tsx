"use client";

import { useState } from "react";
import { Play } from "lucide-react";
import Image from "next/image";
import { KINESCOPE_EMBED_URL } from "@/shared/config";
import { cn } from "@/shared/lib/css";

interface KinescopePlayerProps {
  videoId: string;
  title?: string;
  posterUrl?: string | null;
  className?: string;
  /**
   * Offset в секундах для deep-link'а — если задан, плеер открывается с
   * автоплеем на нужной секунде (Kinescope embed `?t=` query-param). Используется
   * страницей материала когда в URL пришёл `?t=<seconds>` из результата поиска
   * (совпадение по главе видео).
   */
  startSeconds?: number | null;
}

function buildEmbedUrl(videoId: string, autoplay: boolean, startSeconds?: number | null): string {
  const params = new URLSearchParams();
  if (autoplay) params.set("autoplay", "1");
  if (typeof startSeconds === "number" && startSeconds > 0) {
    params.set("t", String(Math.floor(startSeconds)));
  }
  const query = params.toString();
  return `${KINESCOPE_EMBED_URL}/${videoId}${query ? `?${query}` : ""}`;
}

export function KinescopePlayer({
  videoId,
  title = "Видеоурок",
  posterUrl,
  className,
  startSeconds,
}: KinescopePlayerProps) {
  // Если есть startSeconds — пропускаем poster (deep-link → сразу автоплей с момента).
  const hasDeepLink = typeof startSeconds === "number" && startSeconds > 0;
  const [showVideo, setShowVideo] = useState(!posterUrl || hasDeepLink);
  const autoplay = !!posterUrl || hasDeepLink;

  return (
    <div className={cn("relative aspect-video bg-black rounded-xl overflow-hidden", className)}>
      {showVideo ? (
        <iframe
          title={title}
          src={buildEmbedUrl(videoId, autoplay, startSeconds)}
          className="absolute inset-0 w-full h-full border-0"
          allow="autoplay; fullscreen; encrypted-media"
          allowFullScreen
          loading="lazy"
        />
      ) : (
        <button
          type="button"
          aria-label={`Открыть: ${title}`}
          onClick={() => setShowVideo(true)}
          className="absolute inset-0 w-full h-full group"
        >
          <Image src={posterUrl!} alt="" fill unoptimized sizes="100vw" className="object-cover" />
          <div className="absolute inset-0 bg-black/30 group-hover:bg-black/20 transition-colors flex items-center justify-center">
            <div className="size-16 rounded-full bg-primary/90 group-hover:bg-primary flex items-center justify-center transition-transform group-hover:scale-105">
              <Play size={28} className="text-primary-foreground ml-1" fill="currentColor" />
            </div>
          </div>
        </button>
      )}
    </div>
  );
}
