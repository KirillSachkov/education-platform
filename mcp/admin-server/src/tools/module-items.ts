import { z } from 'zod';
import { defineTool } from '../tool.js';

export const eduModuleItemAttachMaterial = defineTool({
  name: 'edu_module_item_attach_material',
  description:
    'Attach an existing material as an item inside a module. Appends at end (sort_key auto-computed). Auto-creates course_materials link if the material is not yet bound to this module\'s course (INV-4). Publishes material.bound event → Redis entitlements sync.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      materialId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ moduleId, materialId }, { client }) => {
    const { data } = await client.post<string>(
      `/api/modules/${moduleId}/materials/${materialId}`,
    );
    return { moduleItemId: data };
  },
});

export const eduModuleItemAttachIssue = defineTool({
  name: 'edu_module_item_attach_issue',
  description:
    'Attach an existing issue as an item inside a module. Issue can only belong to one module at a time. Appends at end.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      issueId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ moduleId, issueId }, { client }) => {
    const { data } = await client.post<string>(`/api/modules/${moduleId}/issues/${issueId}`);
    return { moduleItemId: data };
  },
});

export const eduModuleItemDetach = defineTool({
  name: 'edu_module_item_detach',
  description:
    'Detach an item from a module (removes module_items row). The material/issue itself is not deleted. ReferenceId is the material id or issue id (not module_items.id).',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      referenceId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ moduleId, referenceId }, { client }) => {
    const { data } = await client.delete<string>(
      `/api/modules/${moduleId}/items/${referenceId}`,
    );
    return { detachedItemId: data };
  },
});

export const eduModuleItemAttachQuiz = defineTool({
  name: 'edu_module_item_attach_quiz',
  description:
    'Attach an existing quiz as an item inside a module. Appends at end (use edu_module_item_move to reposition). ' +
    'Auto-creates the derived course_quizzes link and republishes quiz access tags (ST-12 #492 mirror of INV-4). ' +
    'DRAFT quizzes can be attached (hidden from students until published). The module must belong to a course.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      quizId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ moduleId, quizId }, { client }) => {
    const { data } = await client.post<string>(`/api/modules/${moduleId}/quizzes`, { quizId });
    return { moduleItemId: data };
  },
});

export const eduModuleItemMove = defineTool({
  name: 'edu_module_item_move',
  description:
    'Reorder an item inside a module. ReferenceId is material id or issue id. Provide afterSortKey OR beforeSortKey.',
  inputSchema: z
    .object({
      moduleId: z.string().uuid(),
      referenceId: z.string().uuid(),
      afterSortKey: z.string().optional(),
      beforeSortKey: z.string().optional(),
    })
    .strict(),
  handler: async ({ moduleId, referenceId, afterSortKey, beforeSortKey }, { client }) => {
    if (!afterSortKey && !beforeSortKey)
      throw new Error('afterSortKey or beforeSortKey is required');
    const { data } = await client.patch<string>(
      `/api/modules/${moduleId}/items/${referenceId}/move`,
      { afterSortKey: afterSortKey ?? null, beforeSortKey: beforeSortKey ?? null },
    );
    return { movedItemId: data };
  },
});
