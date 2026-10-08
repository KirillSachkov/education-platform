import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduCourseListAdmin = defineTool({
  name: 'edu_course_list_admin',
  description:
    'List ALL courses (DRAFT / PUBLISHED / ARCHIVED) across all authors. Returns id, title, slug, status, authorId, createdAt. Admin-only.',
  inputSchema: z
    .object({
      status: z.enum(['DRAFT', 'PUBLISHED', 'ARCHIVED']).optional(),
      limit: z.number().int().min(1).max(500).optional(),
      offset: z.number().int().min(0).optional(),
    })
    .strict(),
  handler: async ({ status, limit, offset }, { client }) => {
    const { data } = await client.get<unknown[]>('/api/courses/admin-list', {
      query: { status, limit, offset },
    });
    return { courses: data, count: data.length };
  },
});

export const eduCourseCurriculum = defineTool({
  name: 'edu_course_curriculum',
  description:
    'Fetch the curriculum of a course: ordered course_items (modules and projects) with their sort_keys. Needed before moving items or attaching issues.',
  inputSchema: z.object({ courseId: z.string().uuid() }).strict(),
  handler: async ({ courseId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/courses/${courseId}/curriculum`);
    return data;
  },
});

export const eduCourseBuilder = defineTool({
  name: 'edu_course_builder',
  description:
    'Admin-oriented fetch of a course with its modules/projects AND module_items/project_items for the authoring UI. Heaviest read — prefer edu_course_curriculum if you only need the top-level structure. ' +
    'Use `maxItemsPerNode` to cap items per module/project; the response marks truncated nodes with `_truncated=true` and `_totalItems`.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      maxItemsPerNode: z.number().int().min(1).max(500).default(50),
    })
    .strict(),
  handler: async ({ courseId, maxItemsPerNode }, { client }) => {
    const { data } = await client.get<{
      modules?: Array<{ items?: unknown[]; _truncated?: boolean; _totalItems?: number }>;
      projects?: Array<{ items?: unknown[]; _truncated?: boolean; _totalItems?: number }>;
      [key: string]: unknown;
    }>(`/api/courses/${courseId}/builder`);

    const truncateNode = (
      node: { items?: unknown[]; _truncated?: boolean; _totalItems?: number },
    ) => {
      if (Array.isArray(node.items) && node.items.length > maxItemsPerNode) {
        node._totalItems = node.items.length;
        node._truncated = true;
        node.items = node.items.slice(0, maxItemsPerNode);
      }
    };

    (data.modules ?? []).forEach(truncateNode);
    (data.projects ?? []).forEach(truncateNode);

    return data;
  },
});

export const eduCourseDetail = defineTool({
  name: 'edu_course_detail',
  description: 'Course detail DTO (title, description, price, etc.).',
  inputSchema: z.object({ courseId: z.string().uuid() }).strict(),
  handler: async ({ courseId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/courses/${courseId}/detail`);
    return data;
  },
});

export const eduCoursePublish = defineTool({
  name: 'edu_course_publish',
  description:
    'Publish a course — makes it visible in catalog. Course must have at least one PUBLISHED module to be useful. Requires confirm.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ courseId, confirm }, { client }) => {
    requireConfirm('edu_course_publish', courseId, confirm);
    const { data } = await client.post<string>(`/api/courses/${courseId}/publish`);
    return { courseId: data };
  },
});

export const eduCourseArchive = defineTool({
  name: 'edu_course_archive',
  description:
    'Archive a course — hides from catalog. Existing enrollments remain. Reversible via edu_course_restore. Requires confirm.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ courseId, confirm }, { client }) => {
    requireConfirm('edu_course_archive', courseId, confirm);
    const { data } = await client.post<string>(`/api/courses/${courseId}/archive`);
    return { courseId: data };
  },
});

export const eduCourseRestore = defineTool({
  name: 'edu_course_restore',
  description: 'Restore an ARCHIVED course to DRAFT. Requires confirm.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ courseId, confirm }, { client }) => {
    requireConfirm('edu_course_restore', courseId, confirm);
    const { data } = await client.post<string>(`/api/courses/${courseId}/restore`);
    return { courseId: data };
  },
});

export const eduCourseReassignAuthor = defineTool({
  name: 'edu_course_reassign_author',
  description:
    'Transfer course OWNERSHIP to another author (#587). Reassigns the course AND all content owned ' +
    'exclusively by it (modules, projects, issues, materials, quizzes, course-level collections) to ' +
    '`newAuthorId` in one transaction, so the new author fully owns it, can manage it (edit / publish / ' +
    'delete, moderate its comments) and receives "from users" notifications for it. Materials / quizzes ' +
    'SHARED with other courses are NOT moved (kept under the previous author) and returned in ' +
    '`skippedSharedMaterialIds` / `skippedSharedQuizIds`. Student access is NOT changed — access is ' +
    'plan-driven and author-agnostic, so plan holders keep the course. The new author should already hold ' +
    'the `platform-author` role (grant it first via edu_user_set_roles). Returns `{ courseId, newAuthorId, ' +
    'skippedSharedMaterialIds, skippedSharedQuizIds }`. Requires `content.moderate`. Confirm-guarded.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      newAuthorId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ courseId, newAuthorId, confirm }, { client }) => {
    requireConfirm('edu_course_reassign_author', courseId, confirm);
    const { data } = await client.patch<unknown>(`/api/courses/${courseId}/author/`, {
      newAuthorId,
    });
    return data;
  },
});
