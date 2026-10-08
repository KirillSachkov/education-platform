import { z } from 'zod';
import { defineTool } from '../tool.js';

export const eduCourseListPendingListing = defineTool({
  name: 'edu_course_list_pending_listing',
  description:
    'Catalog-moderation queue (#569 "Второй автор"): PUBLISHED courses awaiting catalog-listing approval ' +
    '(`is_catalog_listed = false`). These are visible to their author in "Мои курсы" but NOT yet shown in the ' +
    'public catalog / author portfolio / home until a moderator approves them via edu_course_set_catalog_listing. ' +
    'Cursor-paginated by `(created_at DESC, id DESC)` — pass the returned `nextCursor` to fetch the next page. ' +
    'Each item is enriched with the author display name + avatar URL (best-effort). ' +
    'Returns `{ items: [{ id, authorId, slug, title, description, kind, imageId, createdAt, authorDisplayName, authorAvatarUrl }], nextCursor, totalCount }`. ' +
    'Requires `content.moderate`.',
  inputSchema: z
    .object({
      cursor: z.string().optional(),
      limit: z.number().int().min(1).max(100).optional(),
    })
    .strict(),
  handler: async ({ cursor, limit }, { client }) => {
    const { data } = await client.get<unknown>('/api/courses/admin/pending-listing/', {
      query: { cursor, limit },
    });
    return data;
  },
});

export const eduCourseSetCatalogListing = defineTool({
  name: 'edu_course_set_catalog_listing',
  description:
    'Approve (`listed=true`) or hide (`listed=false`) a course in the public catalog (#569 "Второй автор"). ' +
    'Display-only toggle on `Course.IsCatalogListed` — does NOT affect content access/entitlements, raises no ' +
    'integration event; just controls whether the course card appears in catalog / author portfolio / home. ' +
    'Low-risk and fully reversible, so no confirm guard. Invalidates catalog cache so the change is visible ' +
    'immediately. Returns `{ courseId }`. Requires `content.moderate`.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      listed: z.boolean(),
    })
    .strict(),
  handler: async ({ courseId, listed }, { client }) => {
    const { data } = await client.patch<string>(`/api/courses/${courseId}/catalog-listing/`, {
      listed,
    });
    return { courseId: data };
  },
});
