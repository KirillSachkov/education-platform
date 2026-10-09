import { z } from 'zod';
import { defineTool } from '../tool.js';
import { PlatformApiError } from '../client.js';

export const eduVideoSubtitlesExport = defineTool({
  name: 'edu_video_subtitles_export',
  description:
    'Download .srt transcript for a single video. Returns SRT text (with timestamps and segment text). 404 if no saved transcript exists for the current video version.',
  inputSchema: z.object({ videoId: z.string().uuid() }).strict(),
  handler: async ({ videoId }, { client }) => {
    const { data } = await client.get<string>(
      `/api/videos/${videoId}/subtitles.srt/`,
    );
    return { videoId, srt: data };
  },
});

interface ModuleOverviewItem {
  id: string;
  itemType: 'Material' | 'Issue' | string;
  title: string;
}

interface ModuleOverview {
  id: string;
  title: string;
  items: ModuleOverviewItem[];
}

interface MaterialDetail {
  id: string;
  title: string;
  kind: string;
  videoId?: string | null;
  durationSeconds?: number | null;
}

interface MaterialTranscriptEntry {
  position: number;
  materialId: string;
  title: string;
  kind: string;
  durationSeconds: number | null;
  videoId: string | null;
  status: 'OK' | 'NOT_VIDEO' | 'NO_VIDEO_ASSET' | 'TRANSCRIPT_MISSING' | 'ERROR' | 'SKIPPED_LIMIT';
  errorMessage?: string;
  srt?: string;
}

export const eduModuleTranscriptsExport = defineTool({
  name: 'edu_module_transcripts_export',
  description:
    'Batch export of transcripts for all VIDEO materials in a module. Returns array of {position, materialId, title, durationSeconds, videoId, status, srt?}. ' +
    'status=OK when SRT is available; TRANSCRIPT_MISSING when FileService has no saved transcript for this version; ' +
    'NO_VIDEO_ASSET for material without videoId; ERROR for upstream failures, including access denial. Skips Issue / non-VIDEO materials. ' +
    'Designed for course-audit workflows — one call returns enough context to analyse an entire module. ' +
    'Use `maxVideos` to cap how many VIDEO materials are fetched; remaining materials are listed without SRT and marked status=SKIPPED_LIMIT.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      maxVideos: z.number().int().min(1).max(50).default(10),
    })
    .strict(),
  handler: async ({ moduleId, maxVideos }, { client }) => {
    const { data: overview } = await client.get<ModuleOverview>(
      `/api/modules/${moduleId}/overview/`,
    );

    const materialItems = (overview.items ?? []).filter((i) => i.itemType === 'Material');

    const results: MaterialTranscriptEntry[] = [];
    let position = 0;
    let videosFetched = 0;

    for (const item of materialItems) {
      position += 1;
      const entry: MaterialTranscriptEntry = {
        position,
        materialId: item.id,
        title: item.title,
        kind: 'UNKNOWN',
        durationSeconds: null,
        videoId: null,
        status: 'ERROR',
      };

      try {
        const { data: detail } = await client.get<MaterialDetail>(
          `/api/materials/${item.id}/detail/`,
        );
        entry.kind = detail.kind;
        entry.videoId = detail.videoId ?? null;
        entry.durationSeconds = detail.durationSeconds ?? null;

        if (detail.kind !== 'VIDEO') {
          entry.status = 'NOT_VIDEO';
          results.push(entry);
          continue;
        }

        if (!detail.videoId) {
          entry.status = 'NO_VIDEO_ASSET';
          results.push(entry);
          continue;
        }

        if (videosFetched >= maxVideos) {
          entry.status = 'SKIPPED_LIMIT';
          results.push(entry);
          continue;
        }
        videosFetched += 1;

        try {
          const { data: srt } = await client.get<string>(
            `/api/videos/${detail.videoId}/subtitles.srt/`,
          );
          entry.srt = srt;
          entry.status = 'OK';
        } catch (err) {
          if (err instanceof PlatformApiError && err.info.status === 404) {
            entry.status = 'TRANSCRIPT_MISSING';
          } else {
            entry.status = 'ERROR';
            entry.errorMessage = err instanceof Error ? err.message : String(err);
          }
        }
      } catch (err) {
        entry.status = 'ERROR';
        entry.errorMessage = err instanceof Error ? err.message : String(err);
      }

      results.push(entry);
    }

    const videoItems = results.filter((r) => r.kind === 'VIDEO');

    return {
      moduleId: overview.id,
      moduleTitle: overview.title,
      materialsTotal: materialItems.length,
      videoMaterialsCount: videoItems.length,
      transcriptsAvailable: videoItems.filter((r) => r.status === 'OK').length,
      transcriptsSkippedByLimit: videoItems.filter((r) => r.status === 'SKIPPED_LIMIT').length,
      maxVideosLimit: maxVideos,
      items: results,
    };
  },
});
