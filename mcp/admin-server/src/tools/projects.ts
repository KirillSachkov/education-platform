import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduProjectDetail = defineTool({
  name: 'edu_project_detail',
  description:
    'Project detail with ordered project_items (issues) and their sort_keys. Use to locate issues for move/detach operations.',
  inputSchema: z.object({ projectId: z.string().uuid() }).strict(),
  handler: async ({ projectId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/projects/${projectId}/detail`);
    return data;
  },
});

export const eduProjectUpdate = defineTool({
  name: 'edu_project_update',
  description:
    'Update a project (title, description, detailedDescription). Partial — pass only what changes; wrapper fetches current state and fills the rest because the backend has PUT-semantics on title.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      description: z.string().nullable().optional(),
      detailedDescription: z.string().nullable().optional(),
    })
    .strict(),
  handler: async ({ projectId, ...partial }, { client }) => {
    const { data: current } = await client.get<{
      title: string;
      description: string | null;
      detailedDescription: string | null;
    }>(`/api/projects/${projectId}/detail`);

    const body = {
      title: partial.title ?? current.title,
      description:
        partial.description !== undefined ? partial.description : current.description,
      detailedDescription:
        partial.detailedDescription !== undefined
          ? partial.detailedDescription
          : current.detailedDescription,
    };

    const { data } = await client.patch<string>(`/api/projects/${projectId}`, body);
    return { projectId: data };
  },
});

export const eduProjectPublish = defineTool({
  name: 'edu_project_publish',
  description: 'Publish a project — moves to PUBLISHED state. Requires confirm.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ projectId, confirm }, { client }) => {
    requireConfirm('edu_project_publish', projectId, confirm);
    const { data } = await client.post<string>(`/api/projects/${projectId}/publish`);
    return { projectId: data };
  },
});

export const eduProjectArchive = defineTool({
  name: 'edu_project_archive',
  description:
    'Archive a project — hides from course view. Reversible via edu_project_restore. Requires confirm.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ projectId, confirm }, { client }) => {
    requireConfirm('edu_project_archive', projectId, confirm);
    const { data } = await client.post<string>(`/api/projects/${projectId}/archive`);
    return { projectId: data };
  },
});

export const eduProjectRestore = defineTool({
  name: 'edu_project_restore',
  description: 'Restore an ARCHIVED project to DRAFT. Requires confirm.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ projectId, confirm }, { client }) => {
    requireConfirm('edu_project_restore', projectId, confirm);
    const { data } = await client.post<string>(`/api/projects/${projectId}/restore`);
    return { projectId: data };
  },
});
