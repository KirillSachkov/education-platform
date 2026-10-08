// One-off runner (GitLab #565): upload hardened quiz JSON to prod via PUT /api/quizzes/{id}.
// Run from mcp/admin-server with: npx tsx hardening-runner.ts [--dry-run] <new-quiz.json...>
// Reads .env.prod (PLATFORM_BASE_URL / MCP_CLIENT_ID / MCP_CLIENT_SECRET). NOT committed.
import { readFileSync } from 'node:fs';
import { randomUUID } from 'node:crypto';
import { loadConfig } from './src/config.js';
import { AuthTokenProvider } from './src/auth.js';
import { PlatformClient } from './src/client.js';

// --- load .env.prod into process.env (no dotenv dep) ---
const envText = readFileSync(new URL('./.env.prod', import.meta.url), 'utf8');
for (const line of envText.split('\n')) {
  const m = line.match(/^\s*([A-Z_]+)\s*=\s*(.*)\s*$/);
  if (m) process.env[m[1]] ??= m[2].replace(/^["']|["']$/g, '').trim();
}

const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const files = args.filter((a) => a.endsWith('.json'));
if (!files.length) { console.error('no input json files'); process.exit(2); }

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

interface NewOption { text: string; correct?: boolean }
interface NewQuestion {
  type: string; text: string; difficulty?: string; explanation?: string;
  options?: NewOption[]; referenceAnswer?: string;
}
interface NewQuiz { quizId: string; title: string; passingScorePercent?: number; questions: NewQuestion[] }

function buildQuestionBody(q: NewQuestion) {
  if (q.type === 'EXACT_TEXT' || q.type === 'OPEN_TEXT') {
    return {
      type: q.type, text: q.text,
      options: null, correctOptionIds: null,
      referenceAnswer: q.referenceAnswer ?? null,
      section: null, difficulty: q.difficulty ?? null,
      explanation: q.explanation ?? null,
    };
  }
  const opts = (q.options ?? []).map((o) => ({ id: randomUUID(), text: o.text }));
  const correctOptionIds = (q.options ?? [])
    .map((o, i) => (o.correct ? opts[i].id : null))
    .filter((x): x is string => x !== null);
  return {
    type: q.type, text: q.text,
    options: opts, correctOptionIds,
    referenceAnswer: null, section: null,
    difficulty: q.difficulty ?? null,
    explanation: q.explanation ?? null,
  };
}

const config = loadConfig();
const client = new PlatformClient(config, new AuthTokenProvider(config));

let ok = 0;
let failed = 0;
for (const f of files) {
  const quiz = JSON.parse(readFileSync(f, 'utf8')) as NewQuiz;
  const body = {
    title: quiz.title,
    questions: quiz.questions.map(buildQuestionBody),
    passingScorePercent: quiz.passingScorePercent ?? 70,
    accessType: null,
    levelTestConfig: null,
  };

  if (dryRun) {
    console.log(`DRY ${quiz.quizId}  "${quiz.title}"  ${body.questions.length}q`);
    continue;
  }

  let done = false;
  for (let attempt = 1; attempt <= 6 && !done; attempt++) {
    try {
      await client.put<string>(`/api/quizzes/${quiz.quizId}`, body);
      console.log(`OK  ${quiz.title}  (${body.questions.length}q)`);
      ok++;
      done = true;
    } catch (e: unknown) {
      const status = (e as { info?: { status?: number } })?.info?.status;
      const msg = String(e);
      if ((status === 429 || msg.includes('429')) && attempt < 6) {
        const wait = 2000 * attempt;
        console.warn(`  429 on ${quiz.title} — retry in ${wait}ms`);
        await sleep(wait);
        continue;
      }
      console.error(`FAIL ${quiz.title}: ${msg}`);
      failed++;
      done = true;
    }
  }
  await sleep(1500); // prod write rate-limit ~30/min
}

console.log(`\nDone. ok=${ok} failed=${failed}${dryRun ? ' (dry-run)' : ''}`);
process.exit(failed ? 1 : 0);
