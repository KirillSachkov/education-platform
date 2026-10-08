import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduCollectionDetail = defineTool({
  name: 'edu_collection_detail',
  description:
    'Full collection detail (sections + items with material headers). Admin bypass returns all items ' +
    'regardless of entitlement. Use to inspect what is already in the collection before attaching new items.',
  inputSchema: z.object({ collectionId: z.string().uuid() }).strict(),
  handler: async ({ collectionId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/collections/${collectionId}/detail`);
    return data;
  },
});

export const eduCollectionSectionAdd = defineTool({
  name: 'edu_collection_section_add',
  description:
    'Create a new section in a collection. Appends at the end (sort_key auto-computed by the backend). ' +
    'Caller must own the collection (admin bypass applies). No integration events — sections do not affect ' +
    'access; items added later carry their own entitlement.',
  inputSchema: z
    .object({
      collectionId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      description: z.string().optional(),
    })
    .strict(),
  handler: async ({ collectionId, title, description }, { client }) => {
    const { data } = await client.post<string>(`/api/collections/${collectionId}/sections`, {
      title: title ?? null,
      description: description ?? null,
    });
    return { sectionId: data };
  },
});

export const eduCollectionSectionAddItem = defineTool({
  name: 'edu_collection_section_add_item',
  description:
    'Append an existing material to a collection section. Appends at the end (sort_key auto-computed). ' +
    'Idempotent: backend rejects duplicates (material already in this section). Caller must own the collection ' +
    '(admin bypass applies). The referenced material must already exist and be PUBLISHED to be visible to students.',
  inputSchema: z
    .object({
      collectionId: z.string().uuid(),
      sectionId: z.string().uuid(),
      materialId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ collectionId, sectionId, materialId }, { client }) => {
    const { data } = await client.post<string>(
      `/api/collections/${collectionId}/sections/${sectionId}/items`,
      { materialId },
    );
    return { itemId: data };
  },
});

export const eduCollectionBulkSetItemsAccess = defineTool({
  name: 'edu_collection_bulk_set_items_access',
  description:
    'Bulk-update AccessType for every material in a collection (recursive across all sections). ' +
    'WARNING — blast radius: materials may also live in other courses/collections; this changes ' +
    'their global access there too. Materials owned by a different author are silently skipped ' +
    '(reported in `skippedNotOwnedCount`). Idempotent: materials already at the requested AccessType ' +
    'are counted in `skippedCount` and emit no events. Publishes one `material.access_changed` event ' +
    'per actually-changed material (triggers Redis tag resync). Destructive enough to require confirm.',
  inputSchema: z
    .object({
      collectionId: z.string().uuid(),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'FREE', 'ENROLLED']),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ collectionId, accessType, confirm }, { client }) => {
    requireConfirm('edu_collection_bulk_set_items_access', collectionId, confirm);
    const { data } = await client.patch<{
      updatedCount: number;
      skippedCount: number;
      skippedNotOwnedCount: number;
      totalCount: number;
    }>(`/api/collections/${collectionId}/items/access-type/`, { accessType });
    return data;
  },
});
