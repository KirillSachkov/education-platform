"use client";

import {
  KinescopeInteractivePlayer,
  type KinescopeChapterItem as ChapterPreviewItem,
  type KinescopeInteractivePlayerHandle as KinescopeChapterPreviewHandle,
} from "@/shared/ui/components/kinescope-interactive-player";
import { forwardRef } from "react";

interface KinescopeChapterPreviewProps {
  videoId: string;
  posterUrl?: string | null;
  chapters: ChapterPreviewItem[];
  onTimeUpdate?: (seconds: number) => void;
  onSeekChapter?: (seconds: number) => void;
}

export const KinescopeChapterPreview = forwardRef<
  KinescopeChapterPreviewHandle,
  KinescopeChapterPreviewProps
>(function KinescopeChapterPreview(
  { videoId, posterUrl, chapters, onTimeUpdate, onSeekChapter },
  ref,
) {
  return (
    <KinescopeInteractivePlayer
      ref={ref}
      videoId={videoId}
      posterUrl={posterUrl}
      chapters={chapters}
      onTimeUpdate={onTimeUpdate}
      onSeekChapter={onSeekChapter}
    />
  );
});
