import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

export const eduCourseStudents = defineTool({
  name: 'edu_course_students',
  description:
    'List the student roster of a course (ProgressService). The roster is derived from AccessService grant-holders ' +
    '(so lifetime grant-holders appear immediately, before any engagement), enriched with AuthService profile data. ' +
    'Page/pageSize offset pagination (pageSize max 100, default 20); optional `search` matches name / username / email. ' +
    'Returns a `PaginationResponse<CourseStudentDto>`: items `{ enrollmentId, userId, name, username, email, avatarId, enrolledAt }` ' +
    '+ totalCount/page/pageSize/totalPages. Auth: `courses.manage` + ADMIN | MODERATOR, or AUTHOR who owns the course.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      page: z.number().int().min(1).optional(),
      pageSize: z.number().int().min(1).max(100).optional(),
      search: z.string().optional(),
    })
    .strict(),
  handler: async ({ courseId, page, pageSize, search }, { client }) => {
    const { data } = await client.get<unknown>(`/api/progress/courses/${courseId}/students/`, {
      query: { page, pageSize, search },
    });
    return data;
  },
});

export const eduStudentProgress = defineTool({
  name: 'edu_student_progress',
  description:
    'Per-student progress detail in a course (ProgressService) — ONLY ProgressService facts; course structure is known ' +
    'to the caller and overlaid on top. Returns `StudentCourseProgressDto`: `enrollmentStarted` / `enrolledAt`, ' +
    '`completedMaterials[{ materialId, completedAt }]` (explicitly marked "studied" only — silent track-views excluded), ' +
    'and `issues[{ issueId, projectId, status, reviewStatus, submittedAt, attemptsCount }]`. If the student is a ' +
    'grant-holder who has not started yet, returns 200 with `enrollmentStarted=false` and empty arrays (not 404). ' +
    'Auth: `progress.view` + ADMIN | MODERATOR, or AUTHOR who owns the course.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      userId: z.string().uuid(),
    })
    .strict(),
  handler: async ({ courseId, userId }, { client }) => {
    const { data } = await client.get<unknown>(
      `/api/progress/courses/${courseId}/students/${userId}/progress/`,
    );
    return data;
  },
});

export const eduIssueMarkCompleteForUser = defineTool({
  name: 'edu_issue_mark_complete_for_user',
  description:
    'Mark an issue (task) as completed FOR a student who never submitted any work (ProgressService, staff override). ' +
    'Builds the full progress chain as if the student had reached it themselves (lazy enrollment anchor → project / ' +
    'module progress → issue progress), creates a synthetic approved submission, and runs the normal approve cascade ' +
    '(COMPLETED + XP + project/module rollup + `issue_submission.approved` event). Idempotent — if the issue is already ' +
    'COMPLETED, XP is not double-awarded. Optional `feedback` is recorded on the synthetic review. ' +
    'The MCP server authenticates with a client-credentials service token (no user identity), so the recorded ' +
    'reviewer must be supplied explicitly via `reviewerId` (a real platform admin/moderator/author userId) — ' +
    'without it the backend rejects the call (`value.is.invalid`, reviewerId). Auth: `progress.manage` ' +
    '+ ADMIN | MODERATOR, or AUTHOR who owns the course. Destructive (mutates student progress + awards XP) — ' +
    'requires confirm = issueId.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      issueId: z.string().uuid(),
      userId: z.string().uuid(),
      feedback: z.string().optional(),
      reviewerId: z.string().uuid().optional(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ courseId, issueId, userId, feedback, reviewerId, confirm }, { client }) => {
    requireConfirm('edu_issue_mark_complete_for_user', issueId, confirm);
    await client.post<unknown>(
      `/api/progress/courses/${courseId}/issues/${issueId}/mark-complete-for-user`,
      { userId, feedback, reviewerId },
    );
    return { courseId, issueId, userId, marked: true };
  },
});

export const eduIssueSetStatusForUser = defineTool({
  name: 'edu_issue_set_status_for_user',
  description:
    'Manually set ANY issue (task) progress status for a student (ProgressService, staff override, #518) — ' +
    'full palette NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES / COMPLETED, bypassing the normal ' +
    'submit → review → approve workflow. Builds/ensures the full progress chain (lazy enrollment anchor → project / ' +
    'module progress → issue progress) for the target student, then sets the status. Symmetric XP: COMPLETED awards XP ' +
    'and rolls up project/module; any transition OUT of COMPLETED rolls XP back. Idempotent (target == current → no-op). ' +
    'UNDER_REVIEW / REQUESTED_CHANGES only move the status badge + roll XP back — they do NOT place the work into the ' +
    'review queue (no real submission is created). ' +
    'COMPLETED creates a synthetic approved submission + fires `issue_submission.approved`; because the MCP server ' +
    'authenticates with a client-credentials service token (no user identity), COMPLETED REQUIRES `reviewerId` (a real ' +
    'platform admin/moderator/author userId) — without it COMPLETED is rejected. Other statuses do not need reviewerId. ' +
    'Auth: `progress.manage` + ADMIN | MODERATOR, or AUTHOR who owns the course. Destructive (mutates student progress + ' +
    'awards/revokes XP) — requires confirm = issueId.',
  inputSchema: z
    .object({
      courseId: z.string().uuid(),
      issueId: z.string().uuid(),
      userId: z.string().uuid(),
      targetStatus: z.enum([
        'NOT_STARTED',
        'IN_PROGRESS',
        'UNDER_REVIEW',
        'REQUESTED_CHANGES',
        'COMPLETED',
      ]),
      reviewerId: z.string().uuid().optional(),
      feedback: z.string().optional(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async (
    { courseId, issueId, userId, targetStatus, reviewerId, feedback, confirm },
    { client },
  ) => {
    requireConfirm('edu_issue_set_status_for_user', issueId, confirm);
    await client.put<unknown>(
      `/api/progress/courses/${courseId}/issues/${issueId}/progress-status-for-user/`,
      { userId, targetStatus, reviewerId, feedback },
    );
    return { courseId, issueId, userId, targetStatus, set: true };
  },
});

export const eduMaterialMarkViewedForUser = defineTool({
  name: 'edu_material_mark_viewed_for_user',
  description:
    'Mark a material as studied FOR a student (ProgressService, staff override) — mirror of the self endpoint, but the ' +
    'target is the given `userId`. Raises `MaterialViewedEvent` for the target user → cascades `module_item_progress` ' +
    'and awards XP to the TARGET user. Idempotent by (target userId, materialId). Tier-3 entitlement is checked against ' +
    'the TARGET user (no admin bypass): if the target lacks access to the material the call is rejected 403 and no row ' +
    'is created. Auth: `progress.manage` + ADMIN | MODERATOR, or AUTHOR who owns the material. Destructive (mutates ' +
    'student progress + awards XP) — requires confirm = materialId.',
  inputSchema: z
    .object({
      materialId: z.string().uuid(),
      userId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ materialId, userId, confirm }, { client }) => {
    requireConfirm('edu_material_mark_viewed_for_user', materialId, confirm);
    await client.post<unknown>(`/api/progress/materials/${materialId}/view-for-user`, {
      userId,
    });
    return { materialId, userId, marked: true };
  },
});
