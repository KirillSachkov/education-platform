# Platform Admin MCP

MCP (Model Context Protocol) server wrapping admin HTTP endpoints of the education platform. Lets Claude manage courses, modules, materials, issues, etc. via typed tools — all calls go through admin HTTP endpoints so Postgres, Redis entitlements, and RabbitMQ events stay consistent.

## Setup

```bash
cd mcp/admin-server
npm install
cp .env.example .env      # fill in MCP_CLIENT_SECRET
npm run build
```

### Env vars

| Name | Default (dev) | Description |
|---|---|---|
| `PLATFORM_BASE_URL` | `http://localhost` | Base URL of the nginx-fronted platform |
| `MCP_CLIENT_ID` | `mcp-admin` | OIDC client id (registered by `PlatformConfigSyncService`) |
| `MCP_CLIENT_SECRET` | — | Client secret. Dev value is `DEV-ONLY-mcp-admin-secret`; prod in Infisical as `OPENIDDICT__ADMINAPI__SECRET` |
| `LOG_LEVEL` | `info` | `trace` / `debug` / `info` / `warn` / `error` |

### Smoke check

After `./scripts/dev.sh up` finished starting the platform:

```bash
cd mcp/admin-server
npm run build && npm run smoke
```

Expected output: `{ "ok": true, "count": <n>, "sample": [...] }`.

## Claude Code integration

Add to your `~/.claude/mcp.json` (or project-level `.mcp.json`):

```json
{
  "mcpServers": {
    "platform-admin-dev": {
      "command": "node",
      "args": ["/absolute/path/to/education-platform/mcp/admin-server/dist/index.js"],
      "env": {
        "PLATFORM_BASE_URL": "http://localhost",
        "MCP_CLIENT_ID": "mcp-admin",
        "MCP_CLIENT_SECRET": "DEV-ONLY-mcp-admin-secret"
      }
    },
    "platform-admin-prod": {
      "command": "node",
      "args": ["/absolute/path/to/education-platform/mcp/admin-server/dist/index.js"],
      "env": {
        "PLATFORM_BASE_URL": "https://sachkov-learn.net",
        "MCP_CLIENT_ID": "mcp-admin",
        "MCP_CLIENT_SECRET": "<prod secret from Infisical>"
      }
    }
  }
}
```

Two separate profiles (dev + prod) — so you always know which env you're hitting.

## Architecture

```
src/
├── config.ts               # env loading (zod)
├── logger.ts               # pino → stderr (stdout is for JSON-RPC)
├── auth.ts                 # OIDC client_credentials token fetch + caching
├── client.ts               # HTTP wrapper (auth header, retry, timeout, error mapping)
├── tool.ts                 # defineTool(...) helper
├── zod-to-json-schema.ts   # minimal zod → JSON Schema converter
├── tools/
│   ├── index.ts            # tool registry
│   └── ping.ts             # edu_ping — sanity check
├── smoke.ts                # standalone smoke test
└── index.ts                # MCP server entry (stdio transport)
```

### Consistency guarantees

- **No raw SQL.** Every tool is an HTTP call to an admin endpoint. Admin endpoints publish Wolverine integration events → Redis entitlements stay in sync.
- **`platform-admin` role.** The `mcp-admin` OIDC client gets all permissions via `RolePermissions` mapping.
- **5xx only is retried.** 4xx (validation, auth, not-found) surfaces to Claude immediately.
- **Token caching.** One shared access token (5-min TTL), auto-refreshed 60s before expiry. 401 triggers a single forced refresh + retry.

## Adding new tools

Wrap an existing admin endpoint as a tool under `src/tools/`; when no endpoint exists, add it to the owning service first, then the tool.

Short version:

1. Find or add the backend admin endpoint (`backend/<Service>/src/<Service>.Core/Features/`).
2. Create `src/tools/<group>.ts` with `defineTool(...)`.
3. Register the tool in `src/tools/index.ts`.
4. `npm run build`, reload MCP in Claude.

## Current tool catalog

### Connectivity
| Tool | Description |
|---|---|
| `edu_ping` | Verify MCP can reach admin API and receive an access token |

### Courses (read)
| Tool | Description |
|---|---|
| `edu_course_list_admin` | List ALL courses across authors, all statuses (optional `status` filter) |
| `edu_course_curriculum` | Ordered course_items (modules/projects) with sort_keys |
| `edu_course_builder` | Full admin-UI DTO (modules + projects + their items) |
| `edu_course_detail` | Course metadata |

### Course items (write)
| Tool | Description |
|---|---|
| `edu_course_item_detach` | Remove module/project from course |
| `edu_course_item_move` | Reorder module/project inside course |
| `edu_course_create_module` | Create new module at end of course |
| `edu_course_create_project` | Create new project at end of course |

### Modules
| Tool | Description |
|---|---|
| `edu_module_detail` | Module metadata |
| `edu_module_overview` | Module + ordered module_items |
| `edu_module_update` | Update title/description |

### Module items (write)
| Tool | Description |
|---|---|
| `edu_module_item_attach_material` | Attach material to module (auto-creates course_materials) |
| `edu_module_item_attach_issue` | Attach existing issue to module |
| `edu_module_item_attach_quiz` | Attach existing quiz to module (auto-creates course_quizzes, ST-12) |
| `edu_module_item_detach` | Remove item from module |
| `edu_module_item_move` | Reorder item inside module |

### Quizzes (#544)
| Tool | Description |
|---|---|
| `edu_quiz_list` | Author quiz library (all statuses/purposes; admin sees all) |
| `edu_quiz_detail` | Full author projection incl. correct answers |
| `edu_quiz_create` | Create standalone DRAFT quiz (pass `authorId` via service token!) |
| `edu_quiz_update` | Partial update (full question-set replace; refuses LEVEL_TEST) |
| `edu_quiz_publish` | DRAFT → PUBLISHED (requires ≥1 question, confirm-guarded) |
| `edu_quiz_delete` | Hard delete with cascade (confirm-guarded) |

### Materials

Stored video subtitles remain available through `edu_video_subtitles_export` and
`edu_module_transcripts_export`. These read FileService's saved segments; they never generate
transcripts. The API requires `Videos.MANAGE` and video ownership or platform administrator access.
| Tool | Description |
|---|---|
| `edu_material_detail` | Full material detail |
| `edu_material_delete` | Hard delete |
| `edu_material_update` | Update fields (title, content, accessType, imageId, videoId, previewId) |
| `edu_material_archive` | PUBLISHED → ARCHIVED |
| `edu_material_publish` | DRAFT → PUBLISHED (requires content or video) |

### Projects
| Tool | Description |
|---|---|
| `edu_project_detail` | Project + project_items with sort_keys |
| `edu_project_update` | Update title/description |

### Issues / project items
| Tool | Description |
|---|---|
| `edu_issue_detail` | Full issue detail |
| `edu_issue_create` | Create issue in a project |
| `edu_issue_update` | Update title/content/accessType |
| `edu_issue_archive` | Archive |
| `edu_issue_publish` | Publish |
| `edu_project_issue_attach` | Attach existing orphan issue to a project (idempotent) |
| `edu_project_issue_detach` | Remove issue from project (becomes orphan) |
| `edu_project_issue_move` | Reorder issue inside project |

### Issue audit & batch tooling (#339)
| Tool | Description |
|---|---|
| `edu_issue_list_admin` | List issues across authors/statuses with filters (course/project/module/status/accessType/titleContains) + resolved course+module placement. One call instead of builder + N details |
| `edu_issue_search_admin` | Full-text search issues by title OR content (dedup numbering, phrase/style checks) |
| `edu_course_issues_export` | Export ALL course issues WITH content + placement in one call (audits / batch rewrites) |
| `edu_issue_update_dry_run` | Preview an issue edit — per-field diff + validation, writes nothing. Confirm before `edu_issue_update` |

### AI review prompts & settings (#334 / #356)
Three prompt levels (GLOBAL trusted base prompt + PROJECT guidelines + ISSUE spec) stack into every PR review. All `*_set` tools do a **read-before-write** (omitted fields keep their current value), so there's no accidental clobber.

| Tool | Description |
|---|---|
| `edu_ai_review_settings_get` | Read effective GLOBAL settings: reviewer model slot + base prompt + master switch (`reviewEnabled`), each with Source (CONFIG/DATABASE). Read-only |
| `edu_ai_review_settings_set` | Set GLOBAL base prompt / model / master switch (`Platform.ADMIN`). Read-before-write merge. Works under the MCP service token |
| `edu_project_review_context_get` | Read project-level AI-review guidelines (inspect before overwrite). Read-only |
| `edu_project_set_review_context` | Set project-level guidelines markdown + `isAutoReviewEnabled` |
| `edu_issue_review_spec_get` | Read per-issue AI-review spec (inspect before overwrite). Read-only |
| `edu_issue_set_review_spec` | Set per-issue `authorPrompt` + `reviewAspects` + `isAutoReviewEnabled` (partial merge) |
| `edu_project_review_coverage` | One-call prompt-coverage audit for a whole project (per-issue spec status + lengths). Read-only |
| `edu_course_review_coverage` | One-call prompt-coverage audit for a whole COURSE — rollup summary + per-project + per-issue status (project & module placements). Read-only |

### Plan home pins + onboarding (#397)
Per-plan "home pins" (materials the author pins to the home dashboard of every grant-holder) wrap AccessService endpoints under `/access/plans/{planId}/home-pins/` (Plans.MANAGE + ownership). Get pin IDs via `home_pins_list` before update/reorder/remove. Destructive tools (`home_pins_remove`, `onboarding_reset_all`) require `confirm` = the target ID.

| Tool | Description |
|---|---|
| `home_pins_list` | List a plan's home pins (ordered, ECS-enriched titles) — read-only. Source of `pinId` for the write tools |
| `home_pins_add` | Pin a material to the plan (appended to end). Surfaces 404 material-not-found / 409 already-pinned. Optional `note` (≤500 chars) |
| `home_pins_update_note` | Update/clear the author note on a pin (≤500 chars) |
| `home_pins_reorder` | Fractional reorder of a pin between `beforeId`/`afterId` neighbours |
| `home_pins_remove` | Un-pin a material (material untouched). Requires confirm = pinId |
| `onboarding_reset_all` | **Destructive** — re-run onboarding for ALL grant-holders of a plan (resets each to step 1). Returns `resetCount`; 0 if the flow is disabled/empty. Requires confirm = planId |

### Trial-month credit override (#580)
| Tool | Description |
|---|---|
| `edu_trial_credit_override` | Reopen a user's upgrade-credit window on a trial (FULL_ALL + `trialDurationDays`) plan grant — lets them top up to lifetime with the trial payment credited even after the 14-day grace window. Body `{userId, planId, until?}` (`until` ISO-8601, default now+30d). Requires `plans.grant` + plan ownership (admin bypasses) |

More can be added the same way (see “Adding new tools” above).

## Deployment

Build this MCP server from the same release as the backend it controls.
Configure the API URL and credentials in the deployment environment.
Keep credentials and private curriculum directories outside the source tree.
