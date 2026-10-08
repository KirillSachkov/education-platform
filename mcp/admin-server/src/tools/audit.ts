import { z } from 'zod';
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
import { defineTool } from '../tool.js';

interface ModuleFrontmatter {
  module_iid: number | null;
  module_id: string | null;
  slug: string | null;
  title: string | null;
  priority: string | null;
  status: string | null;
  materials_total: number | null;
  materials_video: number | null;
  duration_total_min: number | null;
  issues_count: number | null;
  tags: string[];
  gitlab_apply_issue: number | null;
  last_session: string | null;
}

interface ModuleDashboardRow extends ModuleFrontmatter {
  file: string;
  parseError?: string;
}

function parseFrontmatterValue(raw: string): unknown {
  const v = raw.trim();
  if (v === '' || v === 'null' || v === '~') return null;
  if (v === 'true') return true;
  if (v === 'false') return false;
  if (v.startsWith('[') && v.endsWith(']')) {
    const inner = v.slice(1, -1).trim();
    if (inner === '') return [];
    return inner.split(',').map((p) => p.trim().replace(/^['"]|['"]$/g, ''));
  }
  if (/^-?\d+(\.\d+)?$/.test(v)) return Number(v);
  return v.replace(/^['"]|['"]$/g, '');
}

function parseFrontmatter(md: string): { data: Record<string, unknown>; error?: string } {
  const lines = md.split(/\r?\n/);
  if (lines[0] !== '---') return { data: {}, error: 'no frontmatter' };
  let end = -1;
  for (let i = 1; i < lines.length; i++) {
    if (lines[i] === '---') {
      end = i;
      break;
    }
  }
  if (end === -1) return { data: {}, error: 'unterminated frontmatter' };
  const out: Record<string, unknown> = {};
  for (let i = 1; i < end; i++) {
    const line = lines[i];
    if (line === undefined) continue;
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;
    const colonIdx = line.indexOf(':');
    if (colonIdx === -1) continue;
    const key = line.slice(0, colonIdx).trim();
    const value = line.slice(colonIdx + 1);
    out[key] = parseFrontmatterValue(value);
  }
  return { data: out };
}

function coerceModuleRow(file: string, data: Record<string, unknown>): ModuleDashboardRow {
  const tagsRaw = data.tags;
  const tags = Array.isArray(tagsRaw)
    ? (tagsRaw.filter((t) => typeof t === 'string') as string[])
    : [];
  return {
    file,
    module_iid: typeof data.module_iid === 'number' ? data.module_iid : null,
    module_id: typeof data.module_id === 'string' ? data.module_id : null,
    slug: typeof data.slug === 'string' ? data.slug : null,
    title: typeof data.title === 'string' ? data.title : null,
    priority: typeof data.priority === 'string' ? data.priority : null,
    status: typeof data.status === 'string' ? data.status : null,
    materials_total: typeof data.materials_total === 'number' ? data.materials_total : null,
    materials_video: typeof data.materials_video === 'number' ? data.materials_video : null,
    duration_total_min:
      typeof data.duration_total_min === 'number' ? data.duration_total_min : null,
    issues_count: typeof data.issues_count === 'number' ? data.issues_count : null,
    tags,
    gitlab_apply_issue:
      typeof data.gitlab_apply_issue === 'number' ? data.gitlab_apply_issue : null,
    last_session: typeof data.last_session === 'string' ? data.last_session : null,
  };
}

export const eduAuditDashboard = defineTool({
  name: 'edu_audit_dashboard',
  description:
    'Read frontmatter from every `*.md` in `{auditDir}/modules/`, aggregate by status / priority / tags. One call → snapshot of the entire restructure-pass state. Pure read on local filesystem — does not touch prod. Use at the start of a session to know where we are without re-reading per-module files.',
  inputSchema: z
    .object({
      auditDir: z
        .string()
        .min(1)
        .describe(
          'Absolute path to a private audit folder (e.g. /srv/private-curriculum/example-audit). Must contain a `modules/` subdirectory.',
        ),
    })
    .strict(),
  handler: async ({ auditDir }) => {
    const modulesDir = join(auditDir, 'modules');
    let entries: string[];
    try {
      entries = await readdir(modulesDir);
    } catch (err) {
      return {
        error: `cannot read modules dir: ${err instanceof Error ? err.message : String(err)}`,
        auditDir,
        modulesDir,
      };
    }

    const moduleFiles = entries.filter((f) => f.endsWith('.md')).sort();
    const rows: ModuleDashboardRow[] = [];

    for (const file of moduleFiles) {
      const fullPath = join(modulesDir, file);
      try {
        const content = await readFile(fullPath, 'utf8');
        const { data, error } = parseFrontmatter(content);
        const row = coerceModuleRow(file, data);
        if (error) row.parseError = error;
        rows.push(row);
      } catch (err) {
        rows.push({
          file,
          module_iid: null,
          module_id: null,
          slug: null,
          title: null,
          priority: null,
          status: null,
          materials_total: null,
          materials_video: null,
          duration_total_min: null,
          issues_count: null,
          tags: [],
          gitlab_apply_issue: null,
          last_session: null,
          parseError: err instanceof Error ? err.message : String(err),
        });
      }
    }

    const byStatus: Record<string, number> = {};
    const byPriority: Record<string, number> = {};
    const tagCounts: Record<string, number> = {};

    for (const r of rows) {
      const status = r.status ?? 'unknown';
      byStatus[status] = (byStatus[status] ?? 0) + 1;
      const priority = r.priority ?? 'unknown';
      byPriority[priority] = (byPriority[priority] ?? 0) + 1;
      for (const t of r.tags) {
        tagCounts[t] = (tagCounts[t] ?? 0) + 1;
      }
    }

    const gitlabApplyIssues = rows
      .map((r) => r.gitlab_apply_issue)
      .filter((v): v is number => typeof v === 'number');

    return {
      auditDir,
      modulesScanned: rows.length,
      byStatus,
      byPriority,
      tagCounts,
      gitlabApplyIssues,
      modules: rows,
    };
  },
});
