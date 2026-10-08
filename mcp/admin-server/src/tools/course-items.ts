import { z } from 'zod';
import { defineTool } from '../tool.js';

export const eduCourseItemDetach = defineTool({
  name: 'edu_course_item_detach',
  description:
    'Detach a module or project from a course (removes the course_items row). The module/project itself is NOT deleted, only its link to this course. ReferenceId is the module id or project id (not course_items.id).',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      referenceId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ courseId, referenceId }, { client }) => {
    const { data } = await client.delete<string>(
      `/api/courses/${courseId}/items/${referenceId}`,
    );
    return { detachedItemId: data };
  },
});

export const eduCourseItemMove = defineTool({
  name: 'edu_course_item_move',
  description:
    'Reorder a course item (module or project). Provide either afterSortKey (place just after this sort_key) or beforeSortKey (place just before). Server recomputes fractional sort_key.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      referenceId: z.string().uuid(),
      afterSortKey: z.string().optional(),
      beforeSortKey: z.string().optional(),
    })
    .strict(),
  handler: async ({ courseId, referenceId, afterSortKey, beforeSortKey }, { client }) => {
    if (!afterSortKey && !beforeSortKey)
      throw new Error('afterSortKey or beforeSortKey is required');
    const { data } = await client.patch<string>(
      `/api/courses/${courseId}/items/${referenceId}/move`,
      { afterSortKey: afterSortKey ?? null, beforeSortKey: beforeSortKey ?? null },
    );
    return { movedItemId: data };
  },
});

export const eduCourseCreateModule = defineTool({
  name: 'edu_course_create_module',
  description: 'Create a new module inside a course (appends at end of course_items).',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      title: z.string().min(1).max(200),
      description: z.string().default(''),
    })
    .strict(),
  handler: async ({ courseId, title, description }, { client }) => {
    const { data } = await client.post<string>(`/api/courses/${courseId}/modules`, {
      title,
      description,
    });
    return { moduleId: data };
  },
});

export const eduCourseCreateProject = defineTool({
  name: 'edu_course_create_project',
  description: 'Create a new project inside a course (appends at end of course_items).',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      title: z.string().min(1).max(200),
      description: z.string().default(''),
    })
    .strict(),
  handler: async ({ courseId, title, description }, { client }) => {
    const { data } = await client.post<string>(`/api/courses/${courseId}/projects`, {
      title,
      description,
    });
    return { projectId: data };
  },
});
