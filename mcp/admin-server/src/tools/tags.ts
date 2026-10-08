import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const tagsList = defineTool({
  name: 'tags_list',
  description:
    'List tags (CANON and/or ALIAS) for the platform author. Cursor-paginated, ordered by title. ' +
    'Returns { items: [{ id, title, slug, kind }], nextCursor, totalCount }. ' +
    'Filter with `search` (title prefix, case-insensitive) and `kind` (CANON = real tags, ALIAS = tags already absorbed into a canonical). ' +
    'Use this to discover tag IDs before merging/renaming/deleting. Usage counts are NOT returned here — inspect tags.entity_tags directly if you need per-tag usage.',
  inputSchema: z
    .object({
      search: z.string().max(50).optional(),
      kind: z.enum(['CANON', 'ALIAS']).optional(),
      limit: z.number().int().min(1).max(50).optional(),
      cursor: z.string().optional(),
    })
    .strict(),
  handler: async ({ search, kind, limit, cursor }, { client }) => {
    const { data } = await client.get<unknown>('/api/tags/', {
      query: { Search: search, Kind: kind, Limit: limit, Cursor: cursor },
    });
    return data;
  },
});

export const tagsMerge = defineTool({
  name: 'tags_merge',
  description:
    'Merge one or more tags INTO a canonical tag as aliases. The alias tags are marked Kind=ALIAS, their entity ' +
    'associations are repointed to the canonical tag (duplicate (entity, tag) pairs are deduplicated), and a ' +
    'TagAlias row is created for each. Publishes `tags.merged` → SearchService reindexes affected documents. ' +
    'Use to consolidate duplicate-concept tags (e.g. "Чистая архитектура" → "Clean Architecture"). ' +
    'Reversible via the DELETE /api/tags/{id}/aliases unmerge endpoint. None of the alias IDs may equal the canonical ID, ' +
    'and an alias tag must not itself already be an alias. Requires confirm = canonicalTagId.',
  inputSchema: z
    .object({
      canonicalTagId: z.string().uuid(),
      aliasTagIds: z.array(z.string().uuid()).min(1),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ canonicalTagId, aliasTagIds, confirm }, { client }) => {
    requireConfirm('tags_merge', canonicalTagId, confirm);
    const { data } = await client.post<string>(`/api/tags/${canonicalTagId}/aliases`, {
      tagIds: aliasTagIds,
    });
    return { canonicalTagId: data, mergedAliasCount: aliasTagIds.length };
  },
});

export const tagsRename = defineTool({
  name: 'tags_rename',
  description:
    'Rename a tag (updates title + auto-derives a new slug via Cyrillic transliteration). Publishes `tags.updated` → ' +
    'SearchService refreshes documents. Use to fix malformed display names (e.g. "Asp Net Core" → "ASP.NET Core"). ' +
    'Non-destructive (no associations change), so no confirm is required.',
  inputSchema: z
    .object({
      tagId: z.string().uuid(),
      title: z.string().min(1).max(150),
    })
    .strict(),
  handler: async ({ tagId, title }, { client }) => {
    const { data } = await client.patch<string>(`/api/tags/${tagId}`, { title });
    return { tagId: data };
  },
});

export const tagsDelete = defineTool({
  name: 'tags_delete',
  description:
    'Hard-delete a tag and cascade-remove its entity associations (entity_tags rows). Publishes `tags.deleted` → ' +
    'SearchService strips the tag from indexed documents. Idempotent — a non-existent ID returns OK. ' +
    'Use to remove junk / accidental tags (e.g. an "Allow" tag mistakenly attached to a module). Requires confirm = tagId.',
  inputSchema: z
    .object({
      tagId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ tagId, confirm }, { client }) => {
    requireConfirm('tags_delete', tagId, confirm);
    const { data } = await client.delete<string>(`/api/tags/${tagId}`);
    return { tagId: data };
  },
});
