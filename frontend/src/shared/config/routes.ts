/**
 * Context tag carried via ?from= search param — tells material view widget where to
 * render "Назад к ..." CTA so the user can get back to the list they came from.
 *
 * Phase 5 single-tenant flat URLs: `slug` field kept in the discriminated union for
 * backwards compatibility with already-emitted `from=` query strings (in user history,
 * notifications, telegram links). It is no longer used to construct the back-link URL —
 * `decodeFromContext` emits flat paths. Future cleanup may drop the field entirely.
 */
export type MaterialFromContext =
  | { kind: "space"; slug?: string }
  | { kind: "space-knowledge-base"; slug?: string }
  | { kind: "course"; courseSlug: string; slug?: string }
  | { kind: "course-knowledge-base"; courseSlug: string; slug?: string }
  | { kind: "collection"; collectionId: string; courseSlug?: string; slug?: string }
  | { kind: "bookmarks"; courseSlug?: string; slug?: string };

function encodeFromContext(from: MaterialFromContext): string {
  switch (from.kind) {
    case "space":
      return `space:${from.slug ?? ""}`;
    case "space-knowledge-base":
      return `space-kb:${from.slug ?? ""}`;
    case "course":
      return `course:${from.slug ?? ""}:${from.courseSlug}`;
    case "course-knowledge-base":
      return `course-kb:${from.slug ?? ""}:${from.courseSlug}`;
    case "collection":
      return from.courseSlug
        ? `collection:${from.slug ?? ""}:${from.courseSlug}:${from.collectionId}`
        : `collection:${from.slug ?? ""}::${from.collectionId}`;
    case "bookmarks":
      return from.courseSlug
        ? `bookmarks:${from.slug ?? ""}:${from.courseSlug}`
        : `bookmarks:${from.slug ?? ""}`;
  }
}

export interface DecodedFromContext {
  href: string;
  label: string;
}

/**
 * Structured parser — returns the raw context object so callers can branch on `kind`
 * (e.g. to inject a "Collection" crumb into the breadcrumb trail when navigating
 * from a collection to a material). For just a "Назад к ..." link, prefer
 * `decodeFromContext`.
 */
export function parseFromContext(raw: string | null): MaterialFromContext | null {
  if (!raw) return null;
  const [kind, ...rest] = raw.split(":");
  switch (kind) {
    case "space":
      return { kind: "space", slug: rest[0] || undefined };
    case "space-kb":
      return { kind: "space-knowledge-base", slug: rest[0] || undefined };
    case "course":
      return rest[1] ? { kind: "course", slug: rest[0] || undefined, courseSlug: rest[1] } : null;
    case "course-kb":
      return rest[1]
        ? { kind: "course-knowledge-base", slug: rest[0] || undefined, courseSlug: rest[1] }
        : null;
    case "collection": {
      const [slug, courseSlug, collectionId] = rest;
      if (!collectionId) return null;
      return courseSlug
        ? { kind: "collection", slug: slug || undefined, courseSlug, collectionId }
        : { kind: "collection", slug: slug || undefined, collectionId };
    }
    case "bookmarks": {
      const [slug, courseSlug] = rest;
      return courseSlug
        ? { kind: "bookmarks", slug: slug || undefined, courseSlug }
        : { kind: "bookmarks", slug: slug || undefined };
    }
    default:
      return null;
  }
}

export function decodeFromContext(raw: string | null): DecodedFromContext | null {
  if (!raw) return null;
  const [kind, ...rest] = raw.split(":");
  switch (kind) {
    case "space":
      return { href: routes.home, label: "На главную" };
    case "space-kb":
      return { href: routes.home, label: "К моему обучению" };
    case "course":
      return rest[1]
        ? {
            href: routes.courseKnowledgeBase(rest[1]),
            label: "К материалам курса",
          }
        : null;
    case "course-kb":
      return rest[1]
        ? {
            href: routes.courseKnowledgeBase(rest[1]),
            label: "К материалам курса",
          }
        : null;
    case "collection": {
      const [, courseSlug, collectionId] = rest;
      if (!collectionId) return null;
      return courseSlug
        ? {
            href: routes.courseCollectionDetail(courseSlug, collectionId),
            label: "К подборке",
          }
        : {
            href: routes.collectionDetail(collectionId),
            label: "К подборке",
          };
    }
    case "bookmarks": {
      const [, courseSlug] = rest;
      return courseSlug
        ? {
            href: routes.courseBookmarks(courseSlug),
            label: "К закладкам",
          }
        : { href: routes.saved, label: "К закладкам" };
    }
    default:
      return null;
  }
}

export const routes = {
  /**
   * Authenticated dashboard. Bottom-nav «Моё обучение» tab on mobile and the
   * post-login destination. The public landing page lives at {@link routes.landing}.
   */
  home: "/home",
  /**
   * Public marketing landing — reachable from the logo, search engines, and
   * legacy bookmarks. Authenticated users hitting `/` are redirected to /home.
   */
  landing: "/",
  login: "/login",
  forgotPassword: "/login/forgot-password",
  resetPassword: "/login/reset-password",
  authError: "/auth-error",
  /** Онбординг привязок (GitHub/Telegram) после OTP-регистрации (#696). */
  onboardingConnections: "/onboarding/connections",

  authorPlans: "/admin/plans" as const,
  authorPlanCreate: "/admin/plans/new" as const,
  authorTrialPlanCreate: "/admin/plans/new-trial" as const,
  authorPlanDetail: (planId: string) => `/admin/plans/${planId}` as const,
  authorPlanEdit: (planId: string) => `/admin/plans/${planId}/edit` as const,

  // --- Courses (single-tenant flat URLs, Phase 5) ---
  /**
   * Personal courses live on «Моё обучение». /courses is a legacy redirect.
   */
  myCourses: "/home",
  courses: "/courses",
  courseOverview: (courseSlug: string) => `/courses/${courseSlug}` as const,
  courseMaterial: (
    courseSlug: string,
    materialId: string,
    opts?: { from?: MaterialFromContext },
  ) => {
    const base = `/courses/${courseSlug}/learn/${materialId}`;
    return opts?.from ? `${base}?from=${encodeFromContext(opts.from)}` : base;
  },
  courseIssue: (courseSlug: string, issueId: string) =>
    `/courses/${courseSlug}/issues/${issueId}` as const,
  /**
   * Module/project no longer have dedicated pages — their routes resolve to the
   * curriculum page with a `section` query param so the matching block opens
   * and scrolls into view. Modules live on the program page, projects on the
   * assignments page (separate sidebar tab).
   */
  courseModule: (courseSlug: string, moduleId: string) =>
    `/courses/${courseSlug}/program?section=${moduleId}` as const,
  courseProject: (courseSlug: string, projectId: string) =>
    `/courses/${courseSlug}/assignments?section=${projectId}` as const,
  courseAssignments: (courseSlug: string) => `/courses/${courseSlug}/assignments` as const,
  /** Вкладка «Тесты» — quiz-элементы программы курса одним списком (#551). */
  courseTests: (courseSlug: string) => `/courses/${courseSlug}/tests` as const,
  courseLessons: (courseSlug: string) => `/courses/${courseSlug}/lessons` as const,
  courseProgram: (courseSlug: string) => `/courses/${courseSlug}/program` as const,
  courseKnowledgeBase: (courseSlug: string) => `/courses/${courseSlug}/knowledge-base` as const,
  /** Detail page of a single course-scoped collection; the list view is part of the knowledge base. */
  courseCollectionDetail: (courseSlug: string, collectionId: string) =>
    `/courses/${courseSlug}/collections/${collectionId}` as const,
  courseBookmarks: (courseSlug: string) => `/courses/${courseSlug}/bookmarks` as const,

  // --- Knowledge base ---
  knowledgeBase: "/knowledge-base",
  knowledgeBaseMaterial: (materialId: string) => `/knowledge-base/${materialId}` as const,
  materialDetail: (materialId: string, opts?: { from?: MaterialFromContext }) => {
    const base = `/knowledge-base/${materialId}`;
    return opts?.from ? `${base}?from=${encodeFromContext(opts.from)}` : base;
  },

  // --- Collections ---
  collections: "/collections",
  collectionDetail: (collectionId: string) => `/collections/${collectionId}` as const,

  // --- Quizzes (студенческая страница standalone-квиза, ST-16 #495) ---
  /** Standalone-цель quiz-строк подборок и прямых ссылок (вне курса). */
  quiz: (quizId: string) => `/quizzes/${quizId}` as const,
  /** Квиз в контексте курса: CourseSidebar + breadcrumbs, как у материалов/задач. */
  courseQuiz: (courseSlug: string, quizId: string) =>
    `/courses/${courseSlug}/quiz/${quizId}` as const,

  // --- Saved (global bookmarks across all courses) ---
  saved: "/saved" as const,

  // --- SEO keyword landings (#530) — public, intent-split content pages,
  //     self-canonical, top-level latin slugs (NOT under /courses/* to avoid the
  //     dynamic [courseSlug] route conflict). ---
  seoCsharp: "/c-sharp" as const,
  seoDotnet: "/dotnet" as const,
  seoAspNetCore: "/asp-net-core" as const,

  // --- Pricing (Phase 5: flat /pricing replaces /@slug/pricing) ---
  pricing: "/pricing" as const,
  pricingPlanDetail: (planSlug: string) => `/pricing/${planSlug}` as const,

  // --- Telegram group join (standalone landing for TelegramJoinReminder, #616) ---
  /**
   * Standalone «вступи в Telegram-группу» — цель приземления out-of-band
   * уведомлений/писем. `?plan=` указывает план, чьи группы показать; без него
   * страница graceful'но ведёт к привязке Telegram / своим планам.
   */
  telegramJoin: (planId?: string) =>
    planId ? (`/telegram/join?plan=${planId}` as const) : ("/telegram/join" as const),

  // --- Access (plans, invites) ---
  invite: (token: string) => `/invite/${token}` as const,
  /** Top-level «Платежи» — подробная история заказов (Phase #414). */
  payments: "/payments" as const,
  /** Top-level «Мои планы» — полный обзор грантов пользователя (Phase #414). */
  myPlans: "/my-plans" as const,

  // --- User & profile ---
  profile: "/profile",
  notifications: "/notifications",
  settings: "/settings",
  settingsAccount: "/settings/account" as const,
  settingsSecurity: "/settings/security" as const,
  settingsIntegrations: "/settings/integrations" as const,
  settingsNotifications: "/settings/notifications" as const,
  settingsSubscriptions: "/settings/subscriptions" as const,
  settingsAppearance: "/settings/appearance" as const,

  // --- Author (teaching) ---
  authorCourses: "/author/courses",
  authorCollections: "/author/collections" as const,
  authorCollectionCreate: "/author/collections/new" as const,
  authorCollectionEdit: (
    collectionId: string,
    opts?: { courseId?: string; courseSlug?: string },
  ) => {
    const params = new URLSearchParams();
    if (opts?.courseId) params.set("courseId", opts.courseId);
    if (opts?.courseSlug) params.set("courseSlug", opts.courseSlug);
    const qs = params.toString();
    return qs
      ? `/author/collections/edit/${collectionId}?${qs}`
      : `/author/collections/edit/${collectionId}`;
  },
  authorKnowledgeBase: "/author/knowledge-base",
  authorKnowledgeBaseCreate: "/author/knowledge-base/new",
  authorKnowledgeBaseNew: (opts?: {
    courseId?: string;
    courseSlug?: string;
    moduleId?: string;
    collectionId?: string;
    sectionId?: string;
  }) => {
    const params = new URLSearchParams();
    if (opts?.courseId) params.set("courseId", opts.courseId);
    if (opts?.courseSlug) params.set("courseSlug", opts.courseSlug);
    if (opts?.moduleId) params.set("moduleId", opts.moduleId);
    if (opts?.collectionId) params.set("collectionId", opts.collectionId);
    if (opts?.sectionId) params.set("sectionId", opts.sectionId);
    const qs = params.toString();
    return qs ? `/author/knowledge-base/new?${qs}` : "/author/knowledge-base/new";
  },
  authorMaterialEdit: (
    materialId: string,
    opts?: { courseId?: string; courseSlug?: string; collectionId?: string },
  ) => {
    const params = new URLSearchParams();
    if (opts?.courseId) params.set("courseId", opts.courseId);
    if (opts?.courseSlug) params.set("courseSlug", opts.courseSlug);
    if (opts?.collectionId) params.set("collectionId", opts.collectionId);
    const qs = params.toString();
    return qs
      ? `/author/knowledge-base/edit/${materialId}?${qs}`
      : `/author/knowledge-base/edit/${materialId}`;
  },
  authorTags: "/author/tags",
  authorCourseBuilder: (courseSlug: string) => `/author/courses/${courseSlug}` as const,
  authorTag: (id: string) => `/author/tags/${id}` as const,
  authorReview: "/author/review",
  authorCatalogModeration: "/author/catalog-moderation" as const,
  authorComments: "/author/comments" as const,
  authorQuizzes: "/author/quizzes" as const,
  /** Библиотека квизов с авто-раскрытым редактором конкретного квиза. */
  authorQuizEdit: (quizId: string) => `/author/quizzes?quiz=${quizId}` as const,
  // --- Admin ---
  onboardingProfile: "/onboarding/profile",
  adminOverview: "/admin/overview",
  adminPlans: "/admin/plans",
  adminAuditLog: "/admin/audit-log",
  adminUsers: "/admin/users",
  adminUserDetail: (id: string) => `/admin/users/${id}` as const,
  adminNotifications: "/admin/notifications",
  adminCampaigns: "/admin/campaigns",
  adminSearch: "/admin/search",
  adminAiUsage: "/admin/ai-usage",
  adminTests: "/admin/tests",
  adminAiModels: "/admin/ai-models",
  adminPayments: "/admin/payments",
  adminPaymentDetail: (id: string) => `/admin/payments/${id}` as const,
} as const;
