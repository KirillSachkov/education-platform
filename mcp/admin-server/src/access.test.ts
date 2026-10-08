import assert from 'node:assert/strict';
import test from 'node:test';
import type { PlatformClient } from './client.js';
import { eduUserRecheckGithubMembership } from './tools/access.js';

const USER_ID = '01900000-0000-7000-8000-000000000001';
const PLAN_ID = '01900000-0000-7000-8000-000000000002';

test('GitHub membership recheck calls the trailing-slash admin endpoint', async () => {
  let capturedPath: string | undefined;
  let capturedBody: unknown;
  const client = {
    post: async (path: string, body: unknown) => {
      capturedPath = path;
      capturedBody = body;
      return { data: { completed: true, status: 'member' } };
    },
  } as unknown as PlatformClient;

  const result = await eduUserRecheckGithubMembership.handler(
    { userId: USER_ID, planId: PLAN_ID, githubLogin: 'octocat' },
    { client },
  );

  assert.equal(
    capturedPath,
    `/api/access/admin/users/${USER_ID}/plans/${PLAN_ID}/github/recheck/`,
  );
  assert.deepEqual(capturedBody, { githubLogin: 'octocat' });
  assert.deepEqual(result, { completed: true, status: 'member' });
});

test('GitHub membership recheck rejects an invalid login', () => {
  const parsed = eduUserRecheckGithubMembership.inputSchema.safeParse({
    userId: USER_ID,
    planId: PLAN_ID,
    githubLogin: '../octocat',
  });

  assert.equal(parsed.success, false);
});
