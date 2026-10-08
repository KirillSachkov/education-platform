import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

// Trainer (#623, epic #568) — admin CRUD for the interview-prep тренажёр content model:
// Track → Topic → TopicBank → TrainerQuestion. All endpoints are role ADMIN; the MCP service
// token already carries admin scope. Backend = TrainerService :8013, nginx maps
// `/api/trainer/` → strip `/api/`.
//
// Glossary surfaced in tool descriptions:
//   - Tier FREE | PAID — фримиум-гейт. PAID = «полный доступ» / Trainer Pro (cap:TRAINER_PRO);
//     FREE-bank доступен любому авторизованному, PAID — только подписчикам Pro (и admin).
//   - Purpose STUDY | MOCK — STUDY banks питают «Изучение»/DRILL/тесты; MOCK banks — пул
//     для симуляции собеса. Create-bank всегда заводит STUDY; смена на MOCK — через update.
//   - A bank now OWNS its questions (#623, own question bank — no longer an ECS-quiz reference):
//     the bank carries tier/difficulty/purpose/order, and TrainerQuestion rows live under it.
//     Add questions with trainer_question_create / list / update / delete.

const TIER = z.enum(['FREE', 'PAID']);
const DIFFICULTY = z.enum(['JUNIOR', 'MIDDLE', 'SENIOR']);
const PURPOSE = z.enum(['STUDY', 'MOCK']);
const DIRECTION = z.enum(['BACKEND', 'FRONTEND', 'FULLSTACK', 'GENERAL']);
const QUESTION_TYPE = z.enum(['SINGLE_CHOICE', 'MULTI_CHOICE', 'EXACT_TEXT', 'OPEN_TEXT']);
const STACK = z.enum(['CSHARP', 'TYPESCRIPT', 'DEVOPS']);

// ── Tracks ───────────────────────────────────────────────────────────────────

export const trainerTrackList = defineTool({
  name: 'trainer_track_list',
  description:
    'List PUBLISHED trainer tracks (GET /trainer/tracks) — the top-level hub selector. Returns each ' +
    'track id, slug, title, stack (CSHARP|TYPESCRIPT|DEVOPS|…), description and topicCount (number of ' +
    'PUBLISHED topics under it). Use the track id to scope trainer_topic_list / trainer_topic_create. ' +
    'NOTE: this is the student-facing list — DRAFT tracks are NOT returned (there is no admin track-list ' +
    'endpoint). Manage tracks with trainer_track_create / trainer_track_update / trainer_track_publish.',
  inputSchema: z.object({}).strict(),
  handler: async (_input, { client }) => {
    const { data } = await client.get<unknown>('/api/trainer/tracks');
    return data;
  },
});

export const trainerTrackCreate = defineTool({
  name: 'trainer_track_create',
  description:
    'Create a trainer track (POST /trainer/tracks) — the top-level hub selector (язык/стек). slug must be ' +
    'unique. stack ∈ CSHARP|TYPESCRIPT|DEVOPS. The track starts as DRAFT — publish separately with ' +
    'trainer_track_publish. Topics attach to a track via trainer_topic_create(trackId). Returns { trackId }.',
  inputSchema: z
    .object({
      slug: z.string().min(1).max(200).describe('Unique URL slug for the track.'),
      title: z.string().min(1).max(300),
      stack: STACK,
      description: z.string().max(4000).optional(),
    })
    .strict(),
  handler: async (input, { client }) => {
    const body = {
      slug: input.slug,
      title: input.title,
      stack: input.stack,
      description: input.description ?? null,
    };
    const { data } = await client.post<unknown>('/api/trainer/tracks/', body);
    return data;
  },
});

export const trainerTrackUpdate = defineTool({
  name: 'trainer_track_update',
  description:
    'Update a track (PUT /trainer/tracks/{id}). slug is IMMUTABLE and not accepted. stack is REQUIRED ' +
    '(re-parsed CSHARP|TYPESCRIPT|DEVOPS). description CLEARS if omitted — pass it to keep it. Returns status.',
  inputSchema: z
    .object({
      trackId: z.string().uuid(),
      title: z.string().min(1).max(300),
      stack: STACK,
      description: z.string().max(4000).optional(),
    })
    .strict(),
  handler: async ({ trackId, ...rest }, { client }) => {
    const body = {
      title: rest.title,
      stack: rest.stack,
      description: rest.description ?? null,
    };
    const { status } = await client.put<unknown>(`/api/trainer/tracks/${trackId}`, body);
    return { trackId, status };
  },
});

export const trainerTrackPublish = defineTool({
  name: 'trainer_track_publish',
  description:
    'Publish a track (POST /trainer/tracks/{id}/publish) — makes it visible in the student hub selector. ' +
    'Idempotent. Requires confirm.',
  inputSchema: z
    .object({
      trackId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ trackId, confirm }, { client }) => {
    requireConfirm('trainer_track_publish', trackId, confirm);
    const { status } = await client.post<unknown>(`/api/trainer/tracks/${trackId}/publish`);
    return { trackId, status };
  },
});

// ── Topics ───────────────────────────────────────────────────────────────────

export const trainerTopicList = defineTool({
  name: 'trainer_topic_list',
  description:
    'Admin topic list incl. DRAFT (GET /trainer/topics/manage), optionally scoped to a track. Returns ' +
    'TopicAdminDto per topic: id, trackId, slug, title, area, description, direction, recommendedCourseId, ' +
    'fallbackCourseId, sortKey, isPublished, bankCount (attached question banks — UI uses it to know if a ' +
    'topic can be deleted), createdAt, updatedAt. Unlike the student GET /trainer/topics this does NOT hide ' +
    'unpublished topics. Pass trackId to filter to one track.',
  inputSchema: z
    .object({
      trackId: z.string().uuid().optional().describe('Filter to topics of this track. Omit for all topics.'),
    })
    .strict(),
  handler: async ({ trackId }, { client }) => {
    const { data } = await client.get<unknown>('/api/trainer/topics/manage', {
      query: trackId ? { trackId } : undefined,
    });
    return data;
  },
});

export const trainerTopicGet = defineTool({
  name: 'trainer_topic_get',
  description:
    'Admin topic card by id incl. DRAFT (GET /trainer/topics/{id}/manage) — full TopicAdminDto metadata ' +
    'plus bankCount. Use before trainer_topic_update / trainer_topic_delete to inspect current state.',
  inputSchema: z.object({ topicId: z.string().uuid() }).strict(),
  handler: async ({ topicId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/trainer/topics/${topicId}/manage`);
    return data;
  },
});

export const trainerTopicCreate = defineTool({
  name: 'trainer_topic_create',
  description:
    'Create a trainer topic (POST /trainer/topics). Topic belongs to a track (trackId, required) and starts ' +
    'as DRAFT — publish separately with trainer_topic_publish. slug must be unique. Topics are admin-owned — ' +
    'no author/owner id is needed. direction (BACKEND|FRONTEND|FULLSTACK|GENERAL) is an optional in-track ' +
    'facet for filtering. recommendedCourseId / fallbackCourseId map the topic to an ECS course for the ' +
    '«подтянуть» CTA (like level-test sections → courses). Returns { topicId }.',
  inputSchema: z
    .object({
      trackId: z.string().uuid(),
      slug: z.string().min(1).max(200).describe('Unique URL slug for the topic.'),
      title: z.string().min(1).max(300),
      area: z.string().min(1).max(200).describe('Topic area / grouping label (required).'),
      description: z.string().max(4000).optional(),
      direction: DIRECTION.optional(),
      recommendedCourseId: z.string().uuid().optional(),
      fallbackCourseId: z.string().uuid().optional(),
    })
    .strict(),
  handler: async (input, { client }) => {
    const body = {
      trackId: input.trackId,
      slug: input.slug,
      title: input.title,
      area: input.area,
      description: input.description ?? null,
      direction: input.direction ?? null,
      recommendedCourseId: input.recommendedCourseId ?? null,
      fallbackCourseId: input.fallbackCourseId ?? null,
    };
    const { data } = await client.post<{ topicId: string }>('/api/trainer/topics/', body);
    return data;
  },
});

export const trainerTopicUpdate = defineTool({
  name: 'trainer_topic_update',
  description:
    'Update a topic (PUT /trainer/topics/{id}). slug is IMMUTABLE and not accepted here. trackId is REQUIRED ' +
    '(re-assigns the track). This is a PUT — fields omitted are sent as null and CLEARED (title/area keep the ' +
    'aggregate invariants, description/direction/course-mappings clear when omitted), so fetch the topic with ' +
    'trainer_topic_get first and pass every field you want to keep. Returns nothing on success.',
  inputSchema: z
    .object({
      topicId: z.string().uuid(),
      trackId: z.string().uuid().describe('Required — re-assigns the topic to this track.'),
      title: z.string().min(1).max(300).optional(),
      area: z.string().min(1).max(200).optional(),
      description: z.string().max(4000).optional(),
      direction: DIRECTION.optional(),
      recommendedCourseId: z.string().uuid().optional(),
      fallbackCourseId: z.string().uuid().optional(),
    })
    .strict(),
  handler: async ({ topicId, ...rest }, { client }) => {
    const body = {
      trackId: rest.trackId,
      title: rest.title ?? null,
      area: rest.area ?? null,
      description: rest.description ?? null,
      direction: rest.direction ?? null,
      recommendedCourseId: rest.recommendedCourseId ?? null,
      fallbackCourseId: rest.fallbackCourseId ?? null,
    };
    const { status } = await client.put<unknown>(`/api/trainer/topics/${topicId}`, body);
    return { topicId, status };
  },
});

export const trainerTopicPublish = defineTool({
  name: 'trainer_topic_publish',
  description:
    'Publish a topic (POST /trainer/topics/{id}/publish) — makes it visible in the student topic list. ' +
    'Requires confirm.',
  inputSchema: z
    .object({
      topicId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ topicId, confirm }, { client }) => {
    requireConfirm('trainer_topic_publish', topicId, confirm);
    const { status } = await client.post<unknown>(`/api/trainer/topics/${topicId}/publish`);
    return { topicId, status };
  },
});

export const trainerTopicUnpublish = defineTool({
  name: 'trainer_topic_unpublish',
  description:
    'Unpublish a topic (POST /trainer/topics/{id}/unpublish) — hides it from the student topic list. ' +
    'Idempotent. Requires confirm.',
  inputSchema: z
    .object({
      topicId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ topicId, confirm }, { client }) => {
    requireConfirm('trainer_topic_unpublish', topicId, confirm);
    const { status } = await client.post<unknown>(`/api/trainer/topics/${topicId}/unpublish`);
    return { topicId, status };
  },
});

export const trainerTopicDelete = defineTool({
  name: 'trainer_topic_delete',
  description:
    'Delete a topic (DELETE /trainer/topics/{id}). BLOCK-ON-CHILDREN: if any question banks are attached the ' +
    'backend refuses with 409 `trainer.topic.has.banks` — delete all banks first via trainer_bank_delete. ' +
    'User mastery/study-state are user-scoped and not cascaded. Irreversible — requires confirm.',
  inputSchema: z
    .object({
      topicId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ topicId, confirm }, { client }) => {
    requireConfirm('trainer_topic_delete', topicId, confirm);
    const { status } = await client.delete<unknown>(`/api/trainer/topics/${topicId}`);
    return { deletedTopicId: topicId, status };
  },
});

// ── Banks ────────────────────────────────────────────────────────────────────

export const trainerBankList = defineTool({
  name: 'trainer_bank_list',
  description:
    'List the question banks of a topic (GET /trainer/topics/{id}/banks), ordered by sortKey. Returns ' +
    'TopicBankAdminDto per bank: id, topicId, tier (FREE|PAID), difficulty (JUNIOR|MIDDLE|SENIOR|null), ' +
    'purpose (STUDY|MOCK), sortKey, questionCount (number of questions in the bank), createdAt.',
  inputSchema: z.object({ topicId: z.string().uuid() }).strict(),
  handler: async ({ topicId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/trainer/topics/${topicId}/banks`);
    return data;
  },
});

export const trainerBankAdd = defineTool({
  name: 'trainer_bank_add',
  description:
    'Create an EMPTY question bank under a topic (POST /trainer/topics/{id}/banks). #623: the bank no ' +
    'longer references an ECS quiz — it OWNS its questions. After creating it, add questions with ' +
    'trainer_question_create (or seed them). The backend verifies the topic exists. tier defaults to FREE ' +
    'if omitted (FREE = open to any authenticated user; PAID = Trainer Pro / «полный доступ» only). ' +
    'difficulty is optional. purpose is always created as STUDY — to make it a MOCK bank, call ' +
    'trainer_bank_update after adding. Returns { bankId }.',
  inputSchema: z
    .object({
      topicId: z.string().uuid(),
      tier: TIER.optional().describe('FREE (default) or PAID (Trainer Pro / «полный доступ»).'),
      difficulty: DIFFICULTY.optional(),
    })
    .strict(),
  handler: async ({ topicId, tier, difficulty }, { client }) => {
    const body = {
      tier: tier ?? null,
      difficulty: difficulty ?? null,
    };
    const { data } = await client.post<{ bankId: string }>(
      `/api/trainer/topics/${topicId}/banks`,
      body,
    );
    return data;
  },
});

export const trainerBankUpdate = defineTool({
  name: 'trainer_bank_update',
  description:
    'Update a question bank by id (PUT /trainer/topic-banks/{bankId}) — flat route, no topic id needed. Sets ' +
    'tier (FREE|PAID), difficulty (JUNIOR|MIDDLE|SENIOR), purpose (STUDY|MOCK). topicId is immutable. NOTE: ' +
    'this is a PUT-style replace — tier defaults to FREE if omitted, difficulty CLEARS if omitted, purpose ' +
    'defaults to STUDY if omitted — so pass every field you want to keep. Use this to flip a STUDY bank to ' +
    'MOCK. Returns { bankId }.',
  inputSchema: z
    .object({
      bankId: z.string().uuid(),
      tier: TIER.optional().describe('FREE (default if omitted) or PAID.'),
      difficulty: DIFFICULTY.optional().describe('Cleared if omitted.'),
      purpose: PURPOSE.optional().describe('STUDY (default if omitted) or MOCK.'),
    })
    .strict(),
  handler: async ({ bankId, tier, difficulty, purpose }, { client }) => {
    const body = {
      tier: tier ?? null,
      difficulty: difficulty ?? null,
      purpose: purpose ?? null,
    };
    const { data } = await client.put<{ bankId: string }>(
      `/api/trainer/topic-banks/${bankId}`,
      body,
    );
    return data;
  },
});

export const trainerBankSetTier = defineTool({
  name: 'trainer_bank_set_tier',
  description:
    'Toggle only the freemium tier of a bank (PATCH /trainer/topic-banks/{bankId}/tier): FREE ↔ PAID, without ' +
    'sending the whole update payload (difficulty/purpose are left untouched). PAID = Trainer Pro / «полный ' +
    'доступ»; FREE = open to any authenticated user. Returns { bankId }.',
  inputSchema: z
    .object({
      bankId: z.string().uuid(),
      tier: TIER.describe('FREE or PAID.'),
    })
    .strict(),
  handler: async ({ bankId, tier }, { client }) => {
    const { data } = await client.patch<{ bankId: string }>(
      `/api/trainer/topic-banks/${bankId}/tier`,
      { tier },
    );
    return data;
  },
});

export const trainerBankDelete = defineTool({
  name: 'trainer_bank_delete',
  description:
    'Delete a question bank by id (DELETE /trainer/topic-banks/{bankId}) — flat route, no topic id needed. ' +
    'The bank and its questions (cascade) are removed. Do this before deleting a topic that still has banks. ' +
    'Irreversible — requires confirm.',
  inputSchema: z
    .object({
      bankId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ bankId, confirm }, { client }) => {
    requireConfirm('trainer_bank_delete', bankId, confirm);
    const { status } = await client.delete<unknown>(`/api/trainer/topic-banks/${bankId}`);
    return { deletedBankId: bankId, status };
  },
});

// ── Questions (#623 — own question bank) ──────────────────────────────────────

const QUESTION_OPTION = z
  .object({
    text: z.string().min(1).describe('Option text.'),
    isCorrect: z.boolean().describe('Whether this option is a correct answer.'),
  })
  .strict();

// Shared input body for create/update. The backend validates per type: choice types need ≥2 options
// (SINGLE = exactly 1 correct, MULTI = ≥1 correct); EXACT_TEXT needs a referenceAnswer; text types
// must NOT carry options.
const questionInputShape = {
  stem: z.string().min(1).describe('Question text (stem).'),
  type: QUESTION_TYPE,
  referenceAnswer: z
    .string()
    .optional()
    .describe('Exact answer for EXACT_TEXT (compared normalized); reference for OPEN_TEXT AI grading.'),
  explanation: z.string().optional().describe('Разбор shown after answering.'),
  difficulty: DIFFICULTY.optional(),
  section: z.string().max(100).optional(),
  options: z
    .array(QUESTION_OPTION)
    .optional()
    .describe('Answer options — required for SINGLE_CHOICE/MULTI_CHOICE, must be omitted for text types.'),
};

function questionInputBody(input: {
  stem: string;
  type: string;
  referenceAnswer?: string;
  explanation?: string;
  difficulty?: string;
  section?: string;
  options?: Array<{ text: string; isCorrect: boolean }>;
}) {
  return {
    stem: input.stem,
    type: input.type,
    referenceAnswer: input.referenceAnswer ?? null,
    explanation: input.explanation ?? null,
    difficulty: input.difficulty ?? null,
    section: input.section ?? null,
    options: input.options ?? null,
  };
}

export const trainerQuestionCreate = defineTool({
  name: 'trainer_question_create',
  description:
    'Create a question in a bank (POST /trainer/topic-banks/{bankId}/questions). #623: questions live in the ' +
    "trainer's own bank (no ECS). type ∈ SINGLE_CHOICE|MULTI_CHOICE|EXACT_TEXT|OPEN_TEXT. Choice types need " +
    "options (SINGLE = exactly 1 correct, MULTI = ≥1 correct); EXACT_TEXT needs referenceAnswer; OPEN_TEXT is " +
    'AI-graded (referenceAnswer optional but recommended). The new question is appended (sortKey). Returns { id }.',
  inputSchema: z.object({ bankId: z.string().uuid(), ...questionInputShape }).strict(),
  handler: async ({ bankId, ...input }, { client }) => {
    const { data } = await client.post<{ id: string }>(
      `/api/trainer/topic-banks/${bankId}/questions`,
      questionInputBody(input),
    );
    return data;
  },
});

export const trainerQuestionList = defineTool({
  name: 'trainer_question_list',
  description:
    'List the FULL questions of a bank (GET /trainer/topic-banks/{bankId}/questions), ordered by sortKey — for ' +
    'the editor. Returns QuestionAdminDto per question incl. options with isCorrect, referenceAnswer, ' +
    'explanation, difficulty, section, sortKey. Admin-only — this is the only place correct answers are exposed.',
  inputSchema: z.object({ bankId: z.string().uuid() }).strict(),
  handler: async ({ bankId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/trainer/topic-banks/${bankId}/questions`);
    return data;
  },
});

export const trainerQuestionUpdate = defineTool({
  name: 'trainer_question_update',
  description:
    'Update a question by id (PUT /trainer/questions/{questionId}). Full replace of stem/type/referenceAnswer/' +
    'explanation/difficulty/section + options. bankId/sortKey are immutable. Same per-type validation as create ' +
    '(use trainer_question_list to fetch current state first). Returns { id }.',
  inputSchema: z.object({ questionId: z.string().uuid(), ...questionInputShape }).strict(),
  handler: async ({ questionId, ...input }, { client }) => {
    const { data } = await client.put<{ id: string }>(
      `/api/trainer/questions/${questionId}`,
      questionInputBody(input),
    );
    return data;
  },
});

export const trainerQuestionDelete = defineTool({
  name: 'trainer_question_delete',
  description:
    'Delete a question by id (DELETE /trainer/questions/{questionId}). Its options cascade. Irreversible — ' +
    'requires confirm.',
  inputSchema: z
    .object({
      questionId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ questionId, confirm }, { client }) => {
    requireConfirm('trainer_question_delete', questionId, confirm);
    const { status } = await client.delete<unknown>(`/api/trainer/questions/${questionId}`);
    return { deletedQuestionId: questionId, status };
  },
});

// ── Mock-interviews (#585 — именованные симуляции собеса) ──────────────────────
// A mock-interview is a named hub card whose session draws a random subsample of QuestionsPerSession
// questions from a CURATED set (explicit questionId refs picked from the trainer's own bank). Build the
// set with trainer_question_bank (picker) → trainer_mock_update (full replace). Legacy topicIds source
// (whole-topic) is also accepted on create. Sessions grade open answers at the end (END_OF_SESSION).

export const trainerMockList = defineTool({
  name: 'trainer_mock_list',
  description:
    'Admin list of mock-interviews incl. DRAFT (GET /trainer/mock-interviews/manage). Returns per mock: ' +
    'id, slug, title, isPublished, questionCount (effective resolvable questions), questionsPerSession. ' +
    'Use the id with trainer_mock_builder / trainer_mock_update / trainer_mock_publish.',
  inputSchema: z.object({}).strict(),
  handler: async (_input, { client }) => {
    const { data } = await client.get<unknown>('/api/trainer/mock-interviews/manage');
    return data;
  },
});

export const trainerMockBuilder = defineTool({
  name: 'trainer_mock_builder',
  description:
    'Admin editor card for one mock-interview (GET /trainer/mock-interviews/{id}/builder): metadata + ' +
    'questionsPerSession + the curated set with each question resolved (stem/type/difficulty + source ' +
    'topic). Dangling refs are skipped. Use before trainer_mock_update to see the current set.',
  inputSchema: z.object({ mockInterviewId: z.string().uuid() }).strict(),
  handler: async ({ mockInterviewId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/trainer/mock-interviews/${mockInterviewId}/builder`);
    return data;
  },
});

export const trainerQuestionBank = defineTool({
  name: 'trainer_question_bank',
  description:
    'Question picker for curating a mock-interview (GET /trainer/question-bank). Lists selectable questions ' +
    'from PUBLISHED topics (optionally filtered by trackId and/or topicId) → their banks → questions: ' +
    '[{ questionId, text, type, difficulty, bankId, topicId, topicTitle, trackId, trackTitle }]. Capped at ' +
    '500. Feed the chosen questionId values (in order) into trainer_mock_update.',
  inputSchema: z
    .object({
      trackId: z.string().uuid().optional().describe('Filter to one track. Omit for all.'),
      topicId: z.string().uuid().optional().describe('Filter to one topic. Omit for all.'),
    })
    .strict(),
  handler: async ({ trackId, topicId }, { client }) => {
    const query: Record<string, string> = {};
    if (trackId) query.trackId = trackId;
    if (topicId) query.topicId = topicId;
    const { data } = await client.get<unknown>('/api/trainer/question-bank', {
      query: Object.keys(query).length > 0 ? query : undefined,
    });
    return data;
  },
});

export const trainerMockCreate = defineTool({
  name: 'trainer_mock_create',
  description:
    'Create a mock-interview (POST /trainer/mock-interviews). slug must be unique; starts as DRAFT. The ' +
    'curated question set is assigned separately via trainer_mock_update (preferred). topicIds is a legacy ' +
    'whole-topic source (pull ALL questions of those topics) — prefer curation. Returns the new id.',
  inputSchema: z
    .object({
      slug: z.string().min(1).max(200).describe('Unique URL slug.'),
      title: z.string().min(1).max(300),
      description: z.string().max(4000).optional(),
      topicIds: z
        .array(z.string().uuid())
        .optional()
        .describe('Legacy whole-topic source. Prefer curating via trainer_mock_update.'),
    })
    .strict(),
  handler: async (input, { client }) => {
    const body = {
      slug: input.slug,
      title: input.title,
      description: input.description ?? null,
      topicIds: input.topicIds ?? null,
      sortIndex: null,
    };
    const { data } = await client.post<unknown>('/api/trainer/mock-interviews/', body);
    return data;
  },
});

export const trainerMockUpdate = defineTool({
  name: 'trainer_mock_update',
  description:
    'Update a mock-interview (PUT /trainer/mock-interviews/{id}) — FULL REPLACE of the curated question set ' +
    '+ session size. title is required. questionsPerSession = size of the random per-session subsample ' +
    '(1..200; omit = whole set). questions = ordered questionId[] (pick them via trainer_question_bank). ' +
    'description clears if omitted. Returns the id.',
  inputSchema: z
    .object({
      mockInterviewId: z.string().uuid(),
      title: z.string().min(1).max(300),
      description: z.string().max(4000).optional(),
      questionsPerSession: z
        .number()
        .int()
        .min(1)
        .max(200)
        .optional()
        .describe('Random subsample size per session. Omit for the whole curated set.'),
      questions: z
        .array(z.string().uuid())
        .describe('Ordered question ids — FULL replace of the curated set.'),
    })
    .strict(),
  handler: async ({ mockInterviewId, ...rest }, { client }) => {
    const body = {
      title: rest.title,
      description: rest.description ?? null,
      questionsPerSession: rest.questionsPerSession ?? null,
      questions: rest.questions.map((questionId) => ({ questionId })),
    };
    const { data } = await client.put<unknown>(
      `/api/trainer/mock-interviews/${mockInterviewId}`,
      body,
    );
    return data;
  },
});

export const trainerMockPublish = defineTool({
  name: 'trainer_mock_publish',
  description:
    'Publish a mock-interview (POST /trainer/mock-interviews/{id}/publish) — makes it visible in the hub. ' +
    'Requires at least one question source (a curated set or legacy topicIds). Idempotent. Requires confirm.',
  inputSchema: z
    .object({
      mockInterviewId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ mockInterviewId, confirm }, { client }) => {
    requireConfirm('trainer_mock_publish', mockInterviewId, confirm);
    const { status } = await client.post<unknown>(
      `/api/trainer/mock-interviews/${mockInterviewId}/publish`,
    );
    return { mockInterviewId, status };
  },
});
