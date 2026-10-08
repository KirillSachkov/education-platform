import { z } from 'zod';
import { defineTool } from '../tool.js';
import { CONFIRM_PARAM_DESCRIPTION, requireConfirm } from '../destructive.js';

const quizOptionSchema = z
  .object({
    id: z
      .string()
      .uuid()
      .describe(
        'Stable option id — generate a UUID yourself so correctOptionIds can reference it.',
      ),
    text: z.string().min(1).max(500),
  })
  .strict();

const quizQuestionSchema = z
  .object({
    id: z.string().uuid().optional(),
    type: z.enum(['SINGLE_CHOICE', 'MULTI_CHOICE', 'OPEN_TEXT', 'EXACT_TEXT']),
    text: z.string().min(1).max(2000),
    options: z
      .array(quizOptionSchema)
      .min(2)
      .max(10)
      .optional()
      .describe('Required for SINGLE_CHOICE / MULTI_CHOICE (2..10); omit for *_TEXT types.'),
    correctOptionIds: z
      .array(z.string().uuid())
      .optional()
      .describe(
        'SINGLE_CHOICE — exactly 1 id; MULTI_CHOICE — 1..N ids (must reference options[].id); omit for *_TEXT types.',
      ),
    referenceAnswer: z
      .string()
      .max(4000)
      .optional()
      .describe(
        'EXACT_TEXT — required exact expected answer (normalization ignores spaces/commas between values); OPEN_TEXT — optional reference for self-check.',
      ),
    explanation: z
      .string()
      .max(2000)
      .optional()
      .describe(
        'Optional rationale ("why this answer is correct") shown to the student in the review AFTER submit — any question type. Not leaked before submit.',
      ),
  })
  .strict();

type QuizQuestionInput = z.infer<typeof quizQuestionSchema>;

interface QuizAuthorView {
  id: string;
  authorId: string;
  title: string;
  status: string;
  accessType: string;
  purpose: string;
  passingScorePercent: number;
  questions: Array<{
    id: string;
    type: string;
    text: string;
    section: string | null;
    difficulty: string | null;
    options: Array<{ id: string; text: string }>;
    correctOptionIds: string[];
    referenceAnswer: string | null;
    explanation: string | null;
  }>;
}

function toQuestionBody(q: QuizQuestionInput) {
  return {
    id: q.id ?? null,
    type: q.type,
    text: q.text,
    options: q.options ?? null,
    correctOptionIds: q.correctOptionIds ?? null,
    referenceAnswer: q.referenceAnswer ?? null,
    explanation: q.explanation ?? null,
  };
}

// Pre-flight check the backend would reject with a 400 anyway — fail fast with a
// clearer message. Kept in the handler (not .superRefine) so the input schema stays
// a plain ZodObject for the JSON-schema converter.
function assertQuestionsValid(questions: QuizQuestionInput[]): void {
  for (const q of questions) {
    if (q.type === 'EXACT_TEXT' && !q.referenceAnswer?.trim()) {
      throw new Error(
        `EXACT_TEXT question "${q.text.slice(0, 60)}" requires a non-empty referenceAnswer`,
      );
    }
  }
}

export const eduQuizList = defineTool({
  name: 'edu_quiz_list',
  description:
    'Author quiz library (GET /quizzes/mine): ALL quizzes of any status and purpose with questionsCount, ' +
    'accessType, usedByMaterialsCount, courseCount. Admin/content-moderator tokens see all quizzes on the ' +
    'platform; an author token sees only its own. ' +
    'LEVEL_TEST quizzes are managed in the /author/level-test UI — do not edit them via MCP.',
  inputSchema: z.object({}).strict(),
  handler: async (_input, { client }) => {
    const { data } = await client.get<unknown>('/api/quizzes/mine');
    return data;
  },
});

export const eduQuizDetail = defineTool({
  name: 'edu_quiz_detail',
  description:
    'Full author projection of a quiz INCLUDING correct answers (correctOptionIds / referenceAnswer) — ' +
    'title, status, accessType, purpose, passingScorePercent, ordered questions.',
  inputSchema: z.object({ quizId: z.string().uuid() }).strict(),
  handler: async ({ quizId }, { client }) => {
    const { data } = await client.get<unknown>(`/api/quizzes/${quizId}`);
    return data;
  },
});

export const eduQuizCreate = defineTool({
  name: 'edu_quiz_create',
  description:
    'Create a standalone quiz in DRAFT (purpose=MATERIAL_CHECK). Pass `authorId` explicitly when calling via ' +
    'the MCP client_credentials flow — without it the service token resolves to Guid.Empty and the quiz will ' +
    'not appear in the author\'s library. Place it into a course with edu_module_item_attach_quiz, then ' +
    'edu_quiz_publish (publish requires ≥1 question). For course content use accessType=ENROLLED to mirror ' +
    'the course materials. Max 50 questions; quiz titles are unique among non-DRAFT quizzes.',
  inputSchema: z
    .object({
      title: z.string().min(1).max(200),
      questions: z.array(quizQuestionSchema).max(50).default([]),
      passingScorePercent: z.number().int().min(0).max(100).default(70),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'ENROLLED']).default('PUBLIC'),
      authorId: z.string().uuid().optional(),
    })
    .strict(),
  handler: async (input, { client }) => {
    assertQuestionsValid(input.questions);
    const body = {
      title: input.title,
      questions: input.questions.map(toQuestionBody),
      passingScorePercent: input.passingScorePercent,
      purpose: 'MATERIAL_CHECK',
      accessType: input.accessType,
      authorId: input.authorId ?? null,
    };
    const { data } = await client.post<string>('/api/quizzes/', body);
    return { quizId: data };
  },
});

export const eduQuizUpdate = defineTool({
  name: 'edu_quiz_update',
  description:
    'Update a quiz. Partial — pass only what changes; the wrapper fetches current state and fills the rest ' +
    'because the backend has PUT-semantics (questions are replaced as a whole set). Works on PUBLISHED quizzes ' +
    'too — changes are visible to students immediately. Refuses LEVEL_TEST quizzes (PUT would wipe their ' +
    'levelTestConfig — edit those in the /author/level-test UI).',
  inputSchema: z
    .object({
      quizId: z.string().uuid(),
      title: z.string().min(1).max(200).optional(),
      questions: z.array(quizQuestionSchema).max(50).optional(),
      passingScorePercent: z.number().int().min(0).max(100).optional(),
      accessType: z.enum(['PUBLIC', 'REGISTERED', 'ENROLLED']).optional(),
    })
    .strict(),
  handler: async ({ quizId, ...partial }, { client }) => {
    if (partial.questions) assertQuestionsValid(partial.questions);
    const { data: current } = await client.get<QuizAuthorView>(`/api/quizzes/${quizId}`);

    if (current.purpose === 'LEVEL_TEST') {
      throw new Error(
        'edu_quiz_update refuses LEVEL_TEST quizzes: PUT replaces the whole quiz and would clear ' +
          'levelTestConfig. Edit the level test in the /author/level-test UI instead.',
      );
    }

    const body = {
      title: partial.title ?? current.title,
      questions: partial.questions
        ? partial.questions.map(toQuestionBody)
        : current.questions.map((q) => ({
            id: q.id,
            type: q.type,
            text: q.text,
            options: q.options,
            correctOptionIds: q.correctOptionIds,
            referenceAnswer: q.referenceAnswer,
            section: q.section,
            difficulty: q.difficulty,
            explanation: q.explanation,
          })),
      passingScorePercent: partial.passingScorePercent ?? current.passingScorePercent,
      // null = "do not change" on the backend; send only an explicit override.
      accessType: partial.accessType ?? null,
      // null = clear levelTestConfig (no-op for MATERIAL_CHECK, safe after the LEVEL_TEST guard above)
      levelTestConfig: null,
    };

    const { data } = await client.put<string>(`/api/quizzes/${quizId}`, body);
    return { quizId: data };
  },
});

export const eduQuizPublish = defineTool({
  name: 'edu_quiz_publish',
  description:
    'Publish a quiz (DRAFT → PUBLISHED). Requires ≥1 question. Sets Redis access tags for the quiz ' +
    '(quiz.published event). Requires confirm.',
  inputSchema: z
    .object({
      quizId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ quizId, confirm }, { client }) => {
    requireConfirm('edu_quiz_publish', quizId, confirm);
    const { data } = await client.post<string>(`/api/quizzes/${quizId}/publish`);
    return { quizId: data };
  },
});

export const eduQuizDelete = defineTool({
  name: 'edu_quiz_delete',
  description:
    'Hard-delete a quiz. Cascades in one transaction: materials.quiz_id reset, course_quizzes / ' +
    'collection_items / module_items rows removed, then quiz.hard_deleted clears Redis tags and student ' +
    'attempts in ProgressService. Irreversible — requires confirm.',
  inputSchema: z
    .object({
      quizId: z.string().uuid(),
      confirm: z.string().describe(CONFIRM_PARAM_DESCRIPTION),
    })
    .strict(),
  handler: async ({ quizId, confirm }, { client }) => {
    requireConfirm('edu_quiz_delete', quizId, confirm);
    const { data } = await client.delete<string>(`/api/quizzes/${quizId}`);
    return { deletedQuizId: data };
  },
});
