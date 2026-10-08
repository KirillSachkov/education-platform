import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduMaterialDetail = defineTool({
  name: 'edu_material_detail',
  description:
    'Full material detail (title, kind, content/videoId, accessType, status). Entitlement-gated for non-admin callers, but platform-admin bypasses.',
  inputSchema: z.object({ materialId: z.string().uuid() }).strict(),
  handler: async ({ materialId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/materials/${materialId}/detail`);
    return data;
  },
});

export const eduMaterialDelete = defineTool({
  name: 'edu_material_delete',
  description:
    'Hard-delete a material. Publishes material.hard_deleted → Redis resource tags cleared, module_items rows cascade-removed, course_materials rows removed. Irreversible — requires confirm.',
  inputSchema: z
    .object({
      materialId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ materialId, confirm }, { client }) => {
    requireConfirm('edu_material_delete', materialId, confirm);
    const { data } = await client.delete<string>(`/api/materials/${materialId}`);
    return { deletedMaterialId: data };
  },
});

export const eduMaterialUpdate = defineTool({
  name: 'edu_material_update',
  description:
    'Update material fields (title, content, kind, accessType, videoId, previewId). Partial — pass only what changes; wrapper fetches current state and fills the rest because the backend has PUT-semantics on title/kind/accessType.',
  inputSchema: z
    .object({
      materialId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      content: z.string().optional(),
      kind: z.enum(['ARTICLE', 'VIDEO', 'NOTE', 'STREAM']).optional(),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'FREE', 'ENROLLED']).optional(),
      videoId: z.string().uuid().nullable().optional(),
      previewId: z.string().uuid().nullable().optional(),
    })
    .strict(),
  handler: async ({ materialId, ...partial }, { client }) => {
    const { data: current } = await client.get<{
      title: string;
      content: string | null;
      kind: string;
      accessType: string;
      imageId: string | null;
      videoId: string | null;
    }>(`/api/materials/${materialId}/detail`);

    const body = {
      title: partial.title ?? current.title,
      content: partial.content ?? current.content,
      kind: partial.kind ?? current.kind,
      accessType: partial.accessType ?? current.accessType,
      videoId: partial.videoId !== undefined ? partial.videoId : current.videoId,
      previewId: partial.previewId !== undefined ? partial.previewId : current.imageId,
    };

    const { data } = await client.patch<string>(`/api/materials/${materialId}`, body);
    return { materialId: data };
  },
});

export const eduMaterialArchive = defineTool({
  name: 'edu_material_archive',
  description: 'Archive a material (soft transition: PUBLISHED → ARCHIVED). Requires confirm.',
  inputSchema: z
    .object({
      materialId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ materialId, confirm }, { client }) => {
    requireConfirm('edu_material_archive', materialId, confirm);
    const { data } = await client.post<string>(`/api/materials/${materialId}/archive`);
    return { materialId: data };
  },
});

export const eduMaterialCreate = defineTool({
  name: 'edu_material_create',
  description:
    'Create a new material; optionally attach to a course/module and publish in the same transaction. ' +
    'Pass `authorId` explicitly when calling via the MCP client_credentials flow — without it the service token ' +
    'resolves to Guid.Empty and the resulting material will be unreachable by enrolled users (Redis tags break). ' +
    'For PublishOnCreate=true the backend enforces the content/videoId invariant.',
  inputSchema: z
    .object({
      title: z.string().min(1).max(200),
      kind: z.enum(['ARTICLE', 'VIDEO', 'NOTE', 'STREAM']).default('ARTICLE'),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'FREE', 'ENROLLED']).default('PUBLIC'),
      content: z.string().optional(),
      videoId: z.string().uuid().optional(),
      previewId: z.string().uuid().optional(),
      courseId: z.string().uuid().optional(),
      moduleId: z.string().uuid().optional(),
      authorId: z.string().uuid().optional(),
      publishOnCreate: z.boolean().default(false),
      notifySubscribers: z.boolean().default(false),
    })
    .strict(),
  handler: async (input, { client }) => {
    const body = {
      title: input.title,
      content: input.content ?? null,
      kind: input.kind,
      accessType: input.accessType,
      videoId: input.videoId ?? null,
      previewId: input.previewId ?? null,
      courseId: input.courseId ?? null,
      moduleId: input.moduleId ?? null,
      publishOnCreate: input.publishOnCreate,
      notifySubscribers: input.notifySubscribers,
      authorId: input.authorId ?? null,
    };
    const { data } = await client.post<string>('/api/materials/', body);
    return { materialId: data };
  },
});

export const eduMaterialPublish = defineTool({
  name: 'edu_material_publish',
  description:
    'Publish a material (DRAFT → PUBLISHED). Requires non-empty content or attached videoId. ' +
    'By default does NOT notify course subscribers (admin/automation context — set notifySubscribers=true to ping them). ' +
    'Requires confirm.',
  inputSchema: z
    .object({
      materialId: z.string().uuid(),
      notifySubscribers: z.boolean().default(false),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ materialId, notifySubscribers, confirm }, { client }) => {
    requireConfirm('edu_material_publish', materialId, confirm);
    const { data } = await client.post<string>(
      `/api/materials/${materialId}/publish`,
      { notifySubscribers },
    );
    return { materialId: data };
  },
});
