import { promises as fs } from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { defineTool } from '../tool.js';

const TARGET_ENTITY_TYPES = ['material', 'course', 'collection', 'issue', 'plan_onboarding_step'] as const;
const USAGE_TYPES = [
  'markdown_image',
  'markdown_file',
  'material_preview',
  'material_video',
  'course_preview',
  'course_video',
  'collection_cover',
  'avatar',
  'issue_attachment',
] as const;

const EXT_TO_CONTENT_TYPE: Record<string, string> = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.pdf': 'application/pdf',
  '.txt': 'text/plain',
  '.md': 'text/markdown',
  '.json': 'application/json',
  '.csv': 'text/csv',
};

interface InitiateResponse {
  assetId: string;
  status: string;
  uploadUrl: string;
  requiredHeaders: Record<string, string>;
  contentUrl: string;
}

export const eduFileUpload = defineTool({
  name: 'edu_file_upload',
  description:
    'Upload a local file to FileService in a single tool call. Runs the full 3-step flow: ' +
    'POST /api/files/uploads (init + presigned URL) → PUT presigned (binary body) → ' +
    'POST /api/files/{id}/complete. Returns {assetId, contentUrl}.\n\n' +
    'Pass `draftId` for create-mode uploads (the file is bound later via edu_draft_assets_bind), ' +
    'or `targetEntityType` + `targetEntityId` for edit-mode (immediate bind). Exactly one of the two ' +
    'binding modes must be provided.\n\n' +
    'contentType is inferred from the file extension when omitted (png/jpg/jpeg/webp/pdf/txt/md/json/csv). ' +
    'fileName defaults to the basename of filePath.',
  inputSchema: z
    .object({
      filePath: z.string().min(1),
      usageType: z.enum(USAGE_TYPES),
      fileName: z.string().optional(),
      contentType: z.string().optional(),
      draftId: z.string().uuid().optional(),
      targetEntityType: z.enum(TARGET_ENTITY_TYPES).optional(),
      targetEntityId: z.string().uuid().optional(),
    })
    .strict()
    .refine(
      (v) =>
        (v.draftId !== undefined) !==
        (v.targetEntityType !== undefined && v.targetEntityId !== undefined),
      {
        message:
          'Provide either `draftId` (create mode) OR both `targetEntityType` + `targetEntityId` (edit mode) — not both.',
      },
    ),
  handler: async (input, { client }) => {
    const buf = await fs.readFile(input.filePath);
    const fileName = input.fileName ?? path.basename(input.filePath);
    const contentType =
      input.contentType ?? EXT_TO_CONTENT_TYPE[path.extname(fileName).toLowerCase()];
    if (!contentType) {
      throw new Error(
        `Cannot infer contentType from ${fileName}; pass it explicitly via contentType param.`,
      );
    }

    const initBody: Record<string, unknown> = {
      fileName,
      contentType,
      size: buf.byteLength,
      usageType: input.usageType,
    };
    if (input.draftId) initBody.draftId = input.draftId;
    if (input.targetEntityType && input.targetEntityId) {
      initBody.targetEntity = { type: input.targetEntityType, id: input.targetEntityId };
    }

    const { data: init } = await client.post<InitiateResponse>('/api/files/uploads', initBody);

    // Step 2: presigned PUT to S3 — bypass PlatformClient (no auth, full URL, binary body).
    const putResp = await fetch(init.uploadUrl, {
      method: 'PUT',
      headers: { ...init.requiredHeaders },
      body: buf,
    });
    if (!putResp.ok) {
      const text = await putResp.text().catch(() => '');
      throw new Error(
        `Presigned PUT to S3 failed: ${putResp.status} ${text.slice(0, 400)}`,
      );
    }

    // Step 3: complete
    await client.post<unknown>(`/api/files/${init.assetId}/complete`, { checksum: null });

    return { assetId: init.assetId, contentUrl: init.contentUrl, size: buf.byteLength };
  },
});

export const eduDraftAssetsBind = defineTool({
  name: 'edu_draft_assets_bind',
  description:
    'Bind a batch of draft-mode assets (uploaded with draftId) to a target entity. Mirrors ' +
    'frontend save-form flow: assets created during a session, then bound atomically when the ' +
    'form is submitted. Handler also RequestDeletes any unlisted draft assets produced during ' +
    'the same draftId — pass the COMPLETE list of asset ids you want to keep.',
  inputSchema: z
    .object({
      draftId: z.string().uuid(),
      targetEntityType: z.enum(TARGET_ENTITY_TYPES),
      targetEntityId: z.string().uuid(),
      assetIds: z.array(z.string().uuid()).min(1).max(100),
    })
    .strict(),
  handler: async ({ draftId, targetEntityType, targetEntityId, assetIds }, { client }) => {
    await client.post<unknown>('/api/draft-assets/bind', {
      draftId,
      targetEntity: { type: targetEntityType, id: targetEntityId },
      assetIds,
    });
    return { boundCount: assetIds.length, draftId };
  },
});
