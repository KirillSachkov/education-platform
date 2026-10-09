# Author Spaces

Platform is single-space from the product/runtime point of view. Authors are
course owners and content contributors inside one platform catalog; they do not
own separate public spaces, storefronts, or plan namespaces.

`AuthorSpace` still exists in AuthService as a legacy compatibility aggregate
for backend integrations that were built before the flat platform model. Do not
use it for new product UI or access decisions.

## Current Model

- Public URLs are flat: `/courses`, `/pricing`.
- Teaching URLs are also platform-level: `/author/courses`,
  `/author/plans`, `/author/materials`, etc.
- Course rows still have `AuthorId`. It means "responsible author / owner of
  this course", not a separate space boundary.
- Access plans are platform plans. FULL_ALL/LEARN_ALL are singleton platform
  plans; COURSE plans bind to concrete courses through `access.plan_courses`.
- Redis entitlements use `plan:all` and `plan:course:{courseId}`. Legacy
  `plan:lifetime:author_X` may be parsed for old data, but is not emitted by
  current grant sync.

## Backend

AuthService keeps `AuthorSpace` endpoints and storage only for compatibility
with older S2S consumers and stored payloads. The default admin bootstrap no
longer creates a frontend-facing author space.

`Shared/Navigation/PlatformLinkBuilder.cs` builds flat URLs for notification
target links. `authorSlug` in payload JSON is silently ignored as a legacy
field so already-stored payloads keep parsing.

Retired `Roadmaps` and `Leaderboard` flags are absent from the compatibility contract and stored
JSON. Their separate corrective migrations preserve all remaining author values and identity data.

## Frontend Routing

All routes live under `(platform)/`:

| URL | Purpose | Layout |
|---|---|---|
| `/` | Public landing (anonymous) / redirect to `/home` (authenticated) | Landing header only |
| `/home` | Authenticated dashboard, bottom-nav «Главная» | AppSidebar |
| `/courses` | Platform course feed, bottom-nav «Курсы» | AppSidebar |
| `/courses/[courseSlug]` | Course overview (enrolled→CourseHome / not→landing) | CourseSidebar |
| `/courses/[courseSlug]/learn/[materialId]` | Material (Article/Video/Note/Stream) | CourseSidebar |
| `/courses/[courseSlug]/program` | Curriculum | CourseSidebar |
| `/knowledge-base/[materialId]` | Legacy material detail with access checks | AppSidebar |
| `/collections/[id]` | Legacy collection detail with access checks | AppSidebar |
| `/courses/[courseSlug]/knowledge-base` | Course material title search and collections | CourseSidebar |
| `/courses/[courseSlug]/collections/[id]` | Course collection detail | CourseSidebar |
| `/pricing`, `/pricing/[planSlug]` | Platform plans | AppSidebar |
| `/author/*` | Teaching mode | AppSidebar |
| `/profile`, `/settings/*`, `/admin/*` | Platform pages | AppSidebar |

`PRIMARY_AUTHOR_SLUG` / `NEXT_PUBLIC_PRIMARY_AUTHOR_SLUG` are not part of the
frontend runtime model anymore. New frontend code must not fetch public data via
author space slug.

## Legacy URL Redirect

`next.config.ts` `redirects()` keeps 308 permanent redirects:

- `/@:slug` -> `/home`
- `/@:slug/home` -> `/home`
- `/@:slug/:path*` -> `/:path*`

Bookmarks, search-engine indices, Telegram notification links, and email
notification links from before 2026-05-23 keep working through the redirect.
The redirect entry is compatibility infrastructure, not an invitation to add new
`/@slug` routes.

## File Layout

```text
frontend/src/app/(app)/
  (landing)/           <- public root /
  (platform)/
    home/
    courses/
      page.tsx         <- platform course feed
      [courseSlug]/
        layout.tsx     <- CourseSidebar
        page.tsx       <- course overview
        assignments/, program/, learn/, issues/, ...
    knowledge-base/[materialId]/
    collections/[collectionId]/  <- legacy detail route
    pricing/[slug]/
    author/, admin/, settings/, profile/, notifications/
```

The old `(space)/spaces/[authorSlug]/` layout group and author-space management
feature are removed from the frontend.
