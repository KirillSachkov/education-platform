import { z } from 'zod';
import { defineTool } from '../tool.js';

/**
 * Tools for configuring the AI PR-review prompts (issue #334). Three levels:
 *   - GLOBAL  reviewer base prompt  (ARS admin, Platform.ADMIN)  — TRUSTED
 *   - PROJECT guidelines markdown   (ECS, Issues.MANAGE)          — reference
 *   - ISSUE   author prompt+aspects (ECS, Issues.MANAGE)          — reference
 * All three are plain markdown and accept embedded code examples / reference
 * solutions. Only the GLOBAL base prompt is TRUSTED (full authority over the
 * reviewer); project/issue prompts are injected as untrusted reference context
 * (their imperative directives are ignored — anti prompt-injection).
 */

interface AiSettings {
  reviewer: {
    model: string;
    temperature: number;
    maxOutputTokens: number;
    timeoutSeconds: number;
    source: string;
  };
  reviewerBasePrompt: { value: string; source: string };
  reviewEnabled: boolean;
  updatedAt: string | null;
}

export const eduAiReviewSettingsGet = defineTool({
  name: 'edu_ai_review_settings_get',
  description:
    'Read the effective GLOBAL AI-review settings: reviewer model slot + the global reviewer base prompt, each with Source (CONFIG = appsettings default, DATABASE = admin override).',
  inputSchema: z.object({}).strict(),
  handler: async (_args, { client }) => {
    const { data } = await client.get<AiSettings>('/api/assignment-review/admin/ai-settings/');
    return data;
  },
});

export const eduAiReviewSettingsSet = defineTool({
  name: 'edu_ai_review_settings_set',
  description:
    'Set the GLOBAL reviewer base prompt and/or model and/or the platform-wide AI-review on/off toggle (Platform.ADMIN). The base prompt is TRUSTED — full authority over every AI review. Plain markdown, max 4000 chars; you may embed coding-standard rules / verdict policy. Omitted fields keep their current effective value; pass reviewerBasePrompt="" to clear (fall back to config default). reviewEnabled is the master switch — when false, no AI reviews run platform-wide regardless of per-project/issue settings. Invalidates the settings cache.',
  inputSchema: z
    .object({
      reviewerBasePrompt: z
        .string()
        .max(4000)
        .optional()
        .describe('Global trusted instruction prepended to every review. "" clears it.'),
      model: z.string().optional().describe('e.g. deepseek/deepseek-v4-pro, anthropic/claude-sonnet-4-6'),
      temperature: z.number().min(0).max(2).optional(),
      maxOutputTokens: z.number().int().positive().optional(),
      timeoutSeconds: z.number().int().positive().optional(),
      reviewEnabled: z
        .boolean()
        .optional()
        .describe('Platform-wide master switch for AI PR review. Omitted = keep current value.'),
    })
    .strict(),
  handler: async (args, { client }) => {
    const { data: current } = await client.get<AiSettings>(
      '/api/assignment-review/admin/ai-settings/',
    );
    const body = {
      reviewer: {
        model: args.model ?? current.reviewer.model,
        temperature: args.temperature ?? current.reviewer.temperature,
        maxOutputTokens: args.maxOutputTokens ?? current.reviewer.maxOutputTokens,
        timeoutSeconds: args.timeoutSeconds ?? current.reviewer.timeoutSeconds,
      },
      reviewerBasePrompt: args.reviewerBasePrompt ?? current.reviewerBasePrompt.value,
      reviewEnabled: args.reviewEnabled ?? current.reviewEnabled,
    };
    await client.put<unknown>('/api/assignment-review/admin/ai-settings/', body);
    return { ok: true, applied: body };
  },
});

export const eduProjectSetReviewContext = defineTool({
  name: 'edu_project_set_review_context',
  description:
    'Set the PROJECT-level review guidelines (Issues.MANAGE). Plain markdown, max 50000 chars — embed project coding standards, expected structure, reference-solution snippets. Injected into every review of issues in this project as reference context. isAutoReviewEnabled toggles auto-AI-review for the project (kept as-is if omitted).',
  inputSchema: z
    .object({
      projectId: z.string().uuid(),
      guidelinesMarkdown: z.string().max(50_000),
      isAutoReviewEnabled: z.boolean().optional(),
    })
    .strict(),
  handler: async ({ projectId, guidelinesMarkdown, isAutoReviewEnabled }, { client }) => {
    const { data: current } = await client.get<{
      isAutoReviewEnabled: boolean;
    } | null>(`/api/projects/${projectId}/review-context`);
    const body = {
      guidelinesMarkdown,
      isAutoReviewEnabled: isAutoReviewEnabled ?? current?.isAutoReviewEnabled ?? true,
    };
    const { data } = await client.put<string>(`/api/projects/${projectId}/review-context`, body);
    return { reviewContextId: data };
  },
});

export const eduIssueSetReviewSpec = defineTool({
  name: 'edu_issue_set_review_spec',
  description:
    'Set the per-ISSUE review spec (Issues.MANAGE): authorPrompt (what to check for this task) + reviewAspects (focus areas). Plain markdown, max 10000 chars each — embed task-specific code examples / acceptance criteria. Injected as reference context. Partial: omitted fields keep their current value.',
  inputSchema: z
    .object({
      issueId: z.string().uuid(),
      authorPrompt: z.string().max(10_000).optional(),
      reviewAspects: z.string().max(10_000).optional(),
      isAutoReviewEnabled: z.boolean().optional(),
    })
    .strict(),
  handler: async ({ issueId, ...partial }, { client }) => {
    const { data: current } = await client.get<{
      authorPrompt: string | null;
      reviewAspects: string | null;
      isAutoReviewEnabled: boolean;
    } | null>(`/api/issues/${issueId}/review-spec`);
    const body = {
      authorPrompt: partial.authorPrompt ?? current?.authorPrompt ?? null,
      reviewAspects: partial.reviewAspects ?? current?.reviewAspects ?? null,
      isAutoReviewEnabled: partial.isAutoReviewEnabled ?? current?.isAutoReviewEnabled ?? true,
    };
    const { data } = await client.put<string>(`/api/issues/${issueId}/review-spec`, body);
    return { issueId: data };
  },
});

// ── P3: read review config before overwriting (no accidental clobber) ────────

export const eduProjectReviewContextGet = defineTool({
  name: 'edu_project_review_context_get',
  description:
    'Read the PROJECT-level AI-review context (guidelines markdown + isAutoReviewEnabled). Returns null if none set yet. ' +
    'Call this before edu_project_set_review_context to avoid clobbering existing guidelines during course-task normalization. Read-only.',
  inputSchema: z.object({ projectId: z.string().uuid() }).strict(),
  handler: async ({ projectId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/projects/${projectId}/review-context`);
    return data ?? { projectId, reviewContext: null };
  },
});

export const eduIssueReviewSpecGet = defineTool({
  name: 'edu_issue_review_spec_get',
  description:
    'Read the per-ISSUE AI-review spec (authorPrompt + reviewAspects + isAutoReviewEnabled). Returns null if none set yet. ' +
    'Call this before edu_issue_set_review_spec to inspect/preserve existing review guidance. Read-only.',
  inputSchema: z.object({ issueId: z.string().uuid() }).strict(),
  handler: async ({ issueId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/issues/${issueId}/review-spec`);
    return data ?? { issueId, reviewSpec: null };
  },
});

// ── P5: bulk review-prompt coverage for a whole project (one call) ───────────

export const eduProjectReviewCoverage = defineTool({
  name: 'edu_project_review_coverage',
  description:
    'Bulk AI-review prompt coverage for a whole project in ONE call: project-context status (hasProjectContext, ' +
    'guidelinesLength, projectIsAutoReviewEnabled) + per-issue review-spec status (hasReviewSpec, authorPromptLength, ' +
    'reviewAspectsLength, isAutoReviewEnabled) for every issue, ordered by sort key. Use to see which tasks still ' +
    'lack prompts before bulk-filling via edu_issue_set_review_spec / edu_project_set_review_context — avoids one ' +
    'edu_issue_review_spec_get round-trip per issue. Lengths are character counts (0 = empty/unset). Read-only.',
  inputSchema: z.object({ projectId: z.string().uuid() }).strict(),
  handler: async ({ projectId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/projects/${projectId}/review-coverage`);
    return data;
  },
});

// ── Course-wide review coverage: whole-course audit in one call ──────────────

export const eduCourseReviewCoverage = defineTool({
  name: 'edu_course_review_coverage',
  description:
    'Bulk AI-review prompt coverage for an ENTIRE course in ONE call: a rollup summary (projectCount, ' +
    'issueCount, issuesWithReviewSpec, issuesWithAuthorPrompt, issuesWithReviewAspects, issuesAutoReviewDisabled, ' +
    'projectsWithContext, projectsAutoReviewDisabled) + per-project blocks, each with project-context status and ' +
    'per-issue review-spec status (hasReviewSpec, authorPromptLength, reviewAspectsLength, isAutoReviewEnabled). ' +
    'Covers issues placed under projects AND under modules. Use to audit which tasks across a whole course still ' +
    'lack prompts before bulk-filling — saves one edu_project_review_coverage round-trip per project. ' +
    'NOTE: the platform-wide master switch (reviewEnabled) is separate — read it via edu_ai_review_settings_get. ' +
    'Lengths are character counts (0 = empty/unset). Read-only.',
  inputSchema: z.object({ courseId: z.string().uuid() }).strict(),
  handler: async ({ courseId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/courses/${courseId}/review-coverage`);
    return data;
  },
});
