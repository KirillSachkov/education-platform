import assert from 'node:assert/strict';
import test from 'node:test';
import { PlatformApiError, type PlatformClient } from './client.js';
import { eduModuleTranscriptsExport, eduVideoSubtitlesExport } from './tools/transcripts.js';
import { allTools } from './tools/index.js';

const VIDEO_ID = '01900000-0000-7000-8000-000000000001';
const MODULE_ID = '01900000-0000-7000-8000-000000000002';
const MATERIAL_ID = '01900000-0000-7000-8000-000000000003';

test('registered SRT export reads FileService without requesting generation', async () => {
  const paths: string[] = [];
  const srt = '1\n00:00:00,001 --> 00:00:01,500\nStored transcript\n';
  const client = { get: async (path: string) => { paths.push(path); return { data: srt }; } } as unknown as PlatformClient;
  assert.ok(allTools.includes(eduVideoSubtitlesExport));
  assert.ok(allTools.includes(eduModuleTranscriptsExport));
  assert.deepEqual(await eduVideoSubtitlesExport.handler({ videoId: VIDEO_ID }, { client }), { videoId: VIDEO_ID, srt });
  assert.deepEqual(paths, [`/api/videos/${VIDEO_ID}/subtitles.srt/`]);
});

test('module export distinguishes absent transcripts from denied access', async () => {
  for (const status of [404, 403]) {
    const paths: string[] = [];
    const client = { get: async (path: string) => {
      paths.push(path);
      if (path === `/api/modules/${MODULE_ID}/overview/`) return { data: {
        id: MODULE_ID, title: 'Synthetic module', items: [{ id: MATERIAL_ID, itemType: 'Material', title: 'Lesson' }],
      } };
      if (path === `/api/materials/${MATERIAL_ID}/detail/`) return { data: { kind: 'VIDEO', videoId: VIDEO_ID } };
      throw new PlatformApiError({ code: 'video.unavailable', message: 'Unavailable', status });
    } } as unknown as PlatformClient;
    const result = await eduModuleTranscriptsExport.handler({ moduleId: MODULE_ID, maxVideos: 1 }, { client });
    assert.equal(result.items[0]?.status, status === 404 ? 'TRANSCRIPT_MISSING' : 'ERROR');
    assert.equal(result.transcriptsAvailable, 0);
    assert.equal(result.items[0]?.srt, undefined);
    assert.deepEqual(paths, [`/api/modules/${MODULE_ID}/overview/`, `/api/materials/${MATERIAL_ID}/detail/`, `/api/videos/${VIDEO_ID}/subtitles.srt/`]);
  }
});
