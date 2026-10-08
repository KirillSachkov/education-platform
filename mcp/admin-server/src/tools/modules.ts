import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduModuleDetail = defineTool({
  name: 'edu_module_detail',
  description: 'Module detail DTO (title, description, status).',
  inputSchema: z.object({ moduleId: z.string().uuid() }).strict(),
  handler: async ({ moduleId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/modules/${moduleId}/detail`);
    return data;
  },
});

export const eduModuleOverview = defineTool({
  name: 'edu_module_overview',
  description:
    'Module with its ordered module_items (materials / issues / quizzes) and their sort_keys. Use this to locate items before moving / detaching them.',
  inputSchema: z.object({ moduleId: z.string().uuid() }).strict(),
  handler: async ({ moduleId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/modules/${moduleId}/overview`);
    return data;
  },
});

export const eduModuleUpdate = defineTool({
  name: 'edu_module_update',
  description:
    'Update a module (title, description, detailedDescription). Partial — pass only what changes; wrapper fetches current state and fills the rest because the backend has PUT-semantics on title.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      description: z.string().nullable().optional(),
      detailedDescription: z.string().nullable().optional(),
    })
    .strict(),
  handler: async ({ moduleId, ...partial }, { client }) => {
    const { data: current } = await client.get<{
      title: string;
      description: string | null;
      detailedDescription: string | null;
    }>(`/api/modules/${moduleId}/detail`);

    const body = {
      title: partial.title ?? current.title,
      description:
        partial.description !== undefined ? partial.description : current.description,
      detailedDescription:
        partial.detailedDescription !== undefined
          ? partial.detailedDescription
          : current.detailedDescription,
    };

    const { data } = await client.patch<string>(`/api/modules/${moduleId}`, body);
    return { moduleId: data };
  },
});

export const eduModulePublish = defineTool({
  name: 'edu_module_publish',
  description:
    'Publish a module — moves it from DRAFT/ARCHIVED to PUBLISHED. Module must be PUBLISHED for students to see it in course program, even if its issues/materials are PUBLISHED. Requires confirm.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ moduleId, confirm }, { client }) => {
    requireConfirm('edu_module_publish', moduleId, confirm);
    const { data } = await client.post<string>(`/api/modules/${moduleId}/publish`);
    return { moduleId: data };
  },
});

export const eduModuleArchive = defineTool({
  name: 'edu_module_archive',
  description:
    'Archive a module — hides it from course program. Existing module_items remain attached. Reversible via edu_module_restore. Requires confirm.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ moduleId, confirm }, { client }) => {
    requireConfirm('edu_module_archive', moduleId, confirm);
    const { data } = await client.post<string>(`/api/modules/${moduleId}/archive`);
    return { moduleId: data };
  },
});

export const eduModuleRestore = defineTool({
  name: 'edu_module_restore',
  description:
    'Restore an ARCHIVED module to DRAFT. To make it visible to students, follow up with edu_module_publish. Requires confirm.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ moduleId, confirm }, { client }) => {
    requireConfirm('edu_module_restore', moduleId, confirm);
    const { data } = await client.post<string>(`/api/modules/${moduleId}/restore`);
    return { moduleId: data };
  },
});
