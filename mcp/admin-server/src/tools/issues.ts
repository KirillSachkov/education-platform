import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduIssueDetail = defineTool({
  name: 'edu_issue_detail',
  description: 'Full issue detail (title, content, accessType, status).',
  inputSchema: z.object({ issueId: z.string().uuid() }).strict(),
  handler: async ({ issueId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/issues/${issueId}/detail`);
    return data;
  },
});

export const eduIssueCreate = defineTool({
  name: 'edu_issue_create',
  description:
    'Create a new issue inside a project. Appends to project_items. Returns new issue id.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      title: z.string().min(1).max(200),
      content: z.string().default(''),
    })
    .strict(),
  handler: async ({ projectId, title, content }, { client }) => {
    const { data } = await client.post<string>(`/api/projects/${projectId}/issues`, {
      title,
      content,
    });
    return { issueId: data };
  },
});

export const eduIssueUpdate = defineTool({
  name: 'edu_issue_update',
  description:
    'Update issue fields (title, content, accessType). Partial — pass only what changes; wrapper fetches current state and fills the rest because the backend has PUT-semantics on title/content/accessType.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      content: z.string().optional(),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'FREE', 'ENROLLED']).optional(),
    })
    .strict(),
  handler: async ({ issueId, ...partial }, { client }) => {
    const { data: current } = await client.get<{
      title: string;
      content: string | null;
      accessType: string;
    }>(`/api/issues/${issueId}/detail`);

    const body = {
      title: partial.title ?? current.title,
      content: partial.content ?? current.content ?? '',
      accessType: partial.accessType ?? current.accessType,
    };

    const { data } = await client.patch<string>(`/api/issues/${issueId}`, body);
    return { issueId: data };
  },
});

export const eduIssueArchive = defineTool({
  name: 'edu_issue_archive',
  description: 'Archive an issue (PUBLISHED → ARCHIVED). Requires confirm.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ issueId, confirm }, { client }) => {
    requireConfirm('edu_issue_archive', issueId, confirm);
    const { data } = await client.post<string>(`/api/issues/${issueId}/archive`);
    return { issueId: data };
  },
});

export const eduIssueDelete = defineTool({
  name: 'edu_issue_delete',
  description:
    'Hard-delete an issue. Publishes issue.hard_deleted (ProgressService clears enrollment/project records) and clears Redis resource-access tags. Admin or issue course author only. Irreversible — requires confirm.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ issueId, confirm }, { client }) => {
    requireConfirm('edu_issue_delete', issueId, confirm);
    const { data } = await client.delete<string>(`/api/issues/${issueId}`);
    return { deletedIssueId: data };
  },
});

export const eduIssuePublish = defineTool({
  name: 'edu_issue_publish',
  description:
    'Publish an issue (DRAFT → PUBLISHED). ' +
    'By default does NOT notify course subscribers (admin/automation context — set notifySubscribers=true to ping them). ' +
    'Requires confirm.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      notifySubscribers: z.boolean().default(false),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ issueId, notifySubscribers, confirm }, { client }) => {
    requireConfirm('edu_issue_publish', issueId, confirm);
    const { data } = await client.post<string>(
      `/api/issues/${issueId}/publish`,
      { notifySubscribers },
    );
    return { issueId: data };
  },
});

export const eduIssueRestore = defineTool({
  name: 'edu_issue_restore',
  description:
    'Restore an ARCHIVED issue back to DRAFT. Use edu_issue_publish afterwards to make it visible again. Requires confirm.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ issueId, confirm }, { client }) => {
    requireConfirm('edu_issue_restore', issueId, confirm);
    const { data } = await client.post<string>(`/api/issues/${issueId}/restore`);
    return { issueId: data };
  },
});

export const eduProjectIssueAttach = defineTool({
  name: 'edu_project_issue_attach',
  description:
    'Attach an EXISTING orphan issue (not bound to any project) to a project. Idempotent — returns existing item id if already attached. Errors with issue.project.already_bound if the issue is attached to a different project.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      issueId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ projectId, issueId }, { client }) => {
    const { data } = await client.post<string>(
      `/api/projects/${projectId}/issues/${issueId}/attach`,
    );
    return { projectItemId: data };
  },
});

export const eduProjectIssueDetach = defineTool({
  name: 'edu_project_issue_detach',
  description:
    'Detach an issue from a project (removes project_items row). The issue itself is not deleted — it becomes an orphan.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      issueId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ projectId, issueId }, { client }) => {
    const { data } = await client.delete<string>(
      `/api/projects/${projectId}/issues/${issueId}`,
    );
    return { detachedItemId: data };
  },
});

export const eduIssueSetInternalMaterials = defineTool({
  name: 'edu_issue_set_internal_materials',
  description:
    'Replace the list of internal materials attached to an issue. Items reference any material (itemType="Material" covers Article/Video/Note/Stream after unification) or a quiz (itemType="Quiz"). Full replace — pass the complete desired list. Empty array clears all attachments.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      items: z.array(
        z.object({
          itemType: z.enum(['Material', 'Quiz']),
          referenceId: z.string().uuid(),
          isRequired: z.boolean().default(false),
        }),
      ),
    })
    .strict(),
  handler: async ({ issueId, items }, { client }) => {
    const { data } = await client.put<string>(
      `/api/issues/${issueId}/internal-materials`,
      { items },
    );
    return { issueId: data };
  },
});

export const eduProjectIssueMove = defineTool({
  name: 'edu_project_issue_move',
  description:
    'Reorder an issue inside a project. Provide afterSortKey OR beforeSortKey from edu_project_detail.',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      issueId: z.string().uuid(),
      afterSortKey: z.string().optional(),
      beforeSortKey: z.string().optional(),
    })
    .strict(),
  handler: async ({ projectId, issueId, afterSortKey, beforeSortKey }, { client }) => {
    if (!afterSortKey && !beforeSortKey)
      throw new Error('afterSortKey or beforeSortKey is required');
    const { data } = await client.patch<string>(
      `/api/projects/${projectId}/issues/${issueId}/move`,
      { afterSortKey: afterSortKey ?? null, beforeSortKey: beforeSortKey ?? null },
    );
    return { movedItemId: data };
  },
});

// ── P2: batch read / search / export (issue audit tooling) ───────────────────
// One backend endpoint (GET /api/issues/admin-list) backs all three tools below.

const issueStatusEnum = z.enum(['DRAFT', 'PUBLISHED', 'ARCHIVED']);
const accessTypeEnum = z.enum(['PUBLIC', 'REGISTERED', 'FREE', 'ENROLLED']);

export const eduIssueListAdmin = defineTool({
  name: 'edu_issue_list_admin',
  description:
    'List issues across all authors/statuses with conjunctive filters (courseId, projectId, moduleId, status, accessType, titleContains). ' +
    'Returns flat rows with project title, resolved course + module placement and sort keys, and internal-material count — ' +
    'so a full-course audit needs one call instead of walking the course builder + per-issue detail. ' +
    'Content is omitted unless includeContent=true (use edu_course_issues_export for content). Read-only.',
  inputSchema: z
    .object({
      courseId: z.string().uuid().optional(),
      projectId: z.string().uuid().optional(),
      moduleId: z.string().uuid().optional(),
      status: issueStatusEnum.optional(),
      accessType: accessTypeEnum.optional(),
      titleContains: z.string().min(1).max(200).optional(),
      includeContent: z.boolean().default(false),
      limit: z.number().int().min(1).max(500).default(200),
      offset: z.number().int().min(0).default(0),
    })
    .strict(),
  handler: async (input, { client }) => {
    const { data } = await client.get<unknown[]>('/api/issues/admin-list', { query: input });
    return { count: Array.isArray(data) ? data.length : 0, issues: data };
  },
});

export const eduIssueSearchAdmin = defineTool({
  name: 'edu_issue_search_admin',
  description:
    'Full-text search issues by title OR content (case-insensitive substring). Useful for duplicate numbering ' +
    '(e.g. two "DS-12"), phrase/style consistency checks, and finding where a concept is assigned. ' +
    'Optionally scope by courseId/projectId/status. Content is omitted unless includeContent=true. Read-only.',
  inputSchema: z
    .object({
      search: z.string().min(1).max(200),
      courseId: z.string().uuid().optional(),
      projectId: z.string().uuid().optional(),
      status: issueStatusEnum.optional(),
      includeContent: z.boolean().default(false),
      limit: z.number().int().min(1).max(500).default(100),
      offset: z.number().int().min(0).default(0),
    })
    .strict(),
  handler: async (input, { client }) => {
    const { data } = await client.get<unknown[]>('/api/issues/admin-list', { query: input });
    return { count: Array.isArray(data) ? data.length : 0, issues: data };
  },
});

export const eduCourseIssuesExport = defineTool({
  name: 'edu_course_issues_export',
  description:
    'Export ALL issues of a course WITH content + placement in one call (includeContent defaults true). ' +
    'The fast path for full-course audits / batch rewrites — replaces builder + N detail calls. ' +
    'Optionally narrow by status. Read-only.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      status: issueStatusEnum.optional(),
      includeContent: z.boolean().default(true),
      limit: z.number().int().min(1).max(500).default(500),
      offset: z.number().int().min(0).default(0),
    })
    .strict(),
  handler: async (input, { client }) => {
    const { data } = await client.get<unknown[]>('/api/issues/admin-list', { query: input });
    return { courseId: input.courseId, count: Array.isArray(data) ? data.length : 0, issues: data };
  },
});

// ── P5: dry-run / diff verification (no write) ───────────────────────────────

interface IssueDetailShape {
  title: string;
  content: string | null;
  accessType: string;
  status: string;
}

export const eduIssueUpdateDryRun = defineTool({
  name: 'edu_issue_update_dry_run',
  description:
    'Preview an issue edit WITHOUT writing it. Fetches the current issue, merges the proposed title/content/accessType ' +
    '(PUT-semantics, same as edu_issue_update), and returns a per-field diff plus validation (title 1..200 chars, ' +
    'content <=50000 chars, accessType in PUBLIC|REGISTERED|FREE|ENROLLED). For content it reports length + preview deltas, ' +
    'not the full body. Use this to confirm a batch rewrite before calling edu_issue_update. Read-only — publishes nothing.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      title: z.string().optional(),
      content: z.string().optional(),
      accessType: accessTypeEnum.optional(),
    })
    .strict(),
  handler: async ({ issueId, ...proposed }, { client }) => {
    const { data: current } = await client.get<IssueDetailShape>(`/api/issues/${issueId}/detail`);

    const next = {
      title: proposed.title ?? current.title,
      content: proposed.content ?? current.content ?? '',
      accessType: proposed.accessType ?? current.accessType,
    };
    const cur = {
      title: current.title,
      content: current.content ?? '',
      accessType: current.accessType,
    };

    const errors: string[] = [];
    if (next.title.length < 1 || next.title.length > 200)
      errors.push(`title length ${next.title.length} out of range 1..200`);
    if (next.content.length > 50000)
      errors.push(`content length ${next.content.length} exceeds 50000`);

    const preview = (s: string) => (s.length > 200 ? `${s.slice(0, 200)}…` : s);
    const diff: Record<string, unknown> = {};
    if (next.title !== cur.title) diff.title = { from: cur.title, to: next.title };
    if (next.accessType !== cur.accessType)
      diff.accessType = { from: cur.accessType, to: next.accessType };
    if (next.content !== cur.content)
      diff.content = {
        changed: true,
        fromLength: cur.content.length,
        toLength: next.content.length,
        fromPreview: preview(cur.content),
        toPreview: preview(next.content),
      };

    return {
      issueId,
      status: current.status,
      changed: Object.keys(diff).length > 0,
      diff,
      validation: { ok: errors.length === 0, errors },
      note: 'Dry-run only — nothing was written. Apply with edu_issue_update.',
    };
  },
});
