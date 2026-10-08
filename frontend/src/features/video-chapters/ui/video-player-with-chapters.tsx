"use client";

import type { MaterialChapterDto } from "@/entities/material";
import {
  KinescopeInteractivePlayer,
  type KinescopeInteractivePlayerHandle,
} from "@/shared/ui/components/kinescope-interactive-player";
import { useRef, useState } from "react";
import { VideoChaptersList } from "./video-chapters-list";

interface VideoPlayerWithChaptersProps {
  videoId: string;
  posterUrl?: string | null;
  /** Главы видео; `undefined`/пустой массив — рендерим только плеер. */
  chapters?: MaterialChapterDto[];
  /** Initial offset (deep-link from `?t=<seconds>`). */
  startSeconds?: number | null;
  className?: string;
}

/**
 * Composes Kinescope player with a YouTube-style chapters list under it.
 * Owns playback time state internally so the parent (e.g. material-view) does not
 * re-render on every `onTimeUpdate` tick.
 *
 * Если у материала нет глав — рендерит только плеер.
 */
export function VideoPlayerWithChapters({
  videoId,
  posterUrl,
  chapters: chaptersInput,
  startSeconds,
  className,
}: VideoPlayerWithChaptersProps) {
  const chapters = chaptersInput ?? EMPTY_CHAPTERS;
  const playerRef = useRef<KinescopeInteractivePlayerHandle | null>(null);
  // Активная глава — единственная derived-зависимость от playback time, поэтому
  // храним только её индекс. setState срабатывает 1-2 раза за главу (не каждый тик),
  // что позволяет не throttle'ить ввод onTimeUpdate.
  const [activeIndex, setActiveIndex] = useState<number>(
    chapters.length > 0 ? findActiveChapterIndex(chapters, startSeconds ?? 0) : -1,
  );

  const kinescopeChapters = chapters.map((chapter, index) => ({
    id: `${index}-${chapter.timeSeconds}`,
    startSeconds: chapter.timeSeconds,
    title: chapter.title,
  }));

  const handleTimeUpdate = (seconds: number) => {
    const nextIndex = findActiveChapterIndex(chapters, seconds);
    if (nextIndex !== activeIndex) setActiveIndex(nextIndex);
  };

  const handleSeek = (seconds: number) => {
    void playerRef.current?.seekTo(seconds);
  };

  return (
    <div className={className}>
      <KinescopeInteractivePlayer
        ref={playerRef}
        videoId={videoId}
        posterUrl={posterUrl}
        chapters={kinescopeChapters}
        startSeconds={startSeconds}
        onTimeUpdate={chapters.length > 0 ? handleTimeUpdate : undefined}
      />
      {chapters.length > 0 && (
        <VideoChaptersList
          chapters={chapters}
          activeIndex={activeIndex}
          onSeek={handleSeek}
          className="mt-3"
        />
      )}
    </div>
  );
}

// Stable reference — снимает лишний re-render `KinescopeInteractivePlayer`
// при отсутствии глав (`chaptersInput === undefined` для stale React Query кеша).
const EMPTY_CHAPTERS: MaterialChapterDto[] = [];

function findActiveChapterIndex(
  chapters: MaterialChapterDto[],
  currentTime: number,
): number {
  // Главы отсортированы по timeSeconds (контракт бэкенда).
  // Активная — последняя у которой timeSeconds <= currentTime.
  let idx = -1;
  for (let i = 0; i < chapters.length; i++) {
    if (chapters[i].timeSeconds <= currentTime) idx = i;
    else break;
  }
  return idx;
}
