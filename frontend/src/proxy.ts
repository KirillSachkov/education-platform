import { auth } from "@/shared/auth/auth";
import { ROLES } from "@/shared/auth/roles";

/** Роуты, доступные без авторизации */
const publicRoutes = [
  "/",
  "/home",
  "/login",
  "/knowledge-base",
  "/legal",
  "/leaderboard",
  "/courses",
  "/roadmaps",
  "/collections",
  "/pricing",
  "/level-test",
  "/c-sharp",
  "/dotnet",
  "/asp-net-core",
  "/yandex_3d24ffdd8bed9484.html",
  // Тренажёр-хаб — аноним просматривает read-only (вкладки, карточки тем, списки вопросов/
  // мок-собесов). Любое действие (старт сессии, ответ, голос, закладка) гейтится на фронте
  // login-CTA, а личные под-роуты (/trainer/session/*, /trainer/progress, /trainer/stats)
  // остаются за authRequiredPatterns ниже. #614 F.
  "/trainer",
  // Precache-target sw.js (offline-шелл, #260) — аноним тоже должен его закешировать (#525).
  "/offline",
  // AI-discovery manifest: a static, non-authoritative map of canonical pages.
  "/llms.txt",
];

/** Префиксы, доступные без авторизации */
const publicPrefixes = [
  "/login/",
  "/knowledge-base/",
  "/invite/",
  "/legal/",
  "/courses/",
  "/roadmaps/",
  "/collections/",
  "/pricing/",
  "/level-test/",
  // Тренажёр: просмотр тем/вопросов (browse). НЕ добавлять широкий "/trainer/" —
  // /trainer/session/*, /trainer/progress, /trainer/stats обязаны оставаться gated. #614 F.
  "/trainer/topics/",
];

/**
 * Паттерны, всегда требующие авторизации — перекрывают publicPrefixes.
 * Сюда входят ТОЛЬКО личные под-роуты курса: мой прогресс, мои закладки,
 * ростер студентов. Весь контент (learn/issues/program/assignments/knowledge-base/collections/
 * modules/projects/roadmap) — публичный с per-item замками: бэкенд отдаёт
 * partial-access (LockReasonResolver), фронт рисует LockCallout / замки + inline
 * «Войти». Форсить /login на контенте не нужно и вредно — это ломало воронку и
 * перекидывало гостя со страницы материала/задания на логин (#385).
 *
 * Course landing (`/courses/[courseSlug]`) тоже публичная.
 *
 * Phase 5 (#308) flat URLs only — legacy /@slug/* and /spaces/* паттерны больше
 * не обрабатываются здесь, потому что next.config.ts `redirects()` 308-редиректит
 * их в flat ещё до того, как proxy.ts увидит запрос.
 */
const authRequiredPatterns: RegExp[] = [/^\/courses\/[^/]+\/(?:progress|bookmarks)(?:\/|$)/];

/** Роли, допущенные к author-маршрутам */
const authorRoles: string[] = [
  ROLES.AUTHOR,
  ROLES.EDITOR,
  ROLES.MODERATOR,
  ROLES.ADMIN,
  ROLES.OWNER,
];

export const proxy = auth((req) => {
  const { pathname } = req.nextUrl;
  const isLoggedIn = !!req.auth;

  // --- Legacy URL redirects ---

  // /catalog | /bookmarks → / (landing). `/courses` is no longer in this list —
  // it is the «Курсы» page (bottom-nav tab) for authenticated users.
  if (
    pathname === "/catalog" ||
    pathname === "/catalog/" ||
    pathname === "/bookmarks" ||
    pathname === "/bookmarks/"
  ) {
    return Response.redirect(new URL("/", req.nextUrl.origin));
  }

  // --- Auth & access control ---

  const isAuthRequired = authRequiredPatterns.some((pattern) => pattern.test(pathname));

  const isPublic =
    !isAuthRequired &&
    (publicRoutes.some((route) => pathname === route || pathname === `${route}/`) ||
      publicPrefixes.some((prefix) => pathname.startsWith(prefix)));

  if (isPublic) {
    return;
  }

  if (!isLoggedIn) {
    const loginUrl = new URL("/login", req.nextUrl.origin);
    // Preserve the current path so user returns here after login
    if (pathname !== "/") {
      loginUrl.searchParams.set("callbackUrl", pathname);
    }
    return Response.redirect(loginUrl);
  }

  // Role-based protection for /admin/* routes
  if (pathname.startsWith("/admin")) {
    const raw = req.auth?.user?.roles;
    const roles = Array.isArray(raw) ? raw : raw ? [raw] : [];
    if (!roles.includes(ROLES.ADMIN) && !roles.includes(ROLES.OWNER)) {
      return Response.redirect(new URL("/", req.nextUrl.origin));
    }
  }

  // Role-based protection for /author/* routes
  if (pathname.startsWith("/author")) {
    const raw = req.auth?.user?.roles;
    const roles = Array.isArray(raw) ? raw : raw ? [raw] : [];
    const hasAuthorAccess = roles.some((r) => authorRoles.includes(r));
    if (!hasAuthorAccess) {
      return Response.redirect(new URL("/", req.nextUrl.origin));
    }

    if (
      (pathname === "/author/tags" || pathname.startsWith("/author/tags/")) &&
      !roles.includes(ROLES.ADMIN) &&
      !roles.includes(ROLES.OWNER)
    ) {
      return Response.redirect(new URL("/author/courses", req.nextUrl.origin));
    }
  }
});

export const config = {
  matcher: [
    // sw\.js исключён явно (#525): service-worker-скрипт обязан отдаваться без
    // редиректов — браузер запрещает SW behind a redirect, и аноним получал
    // 302 → /login → регистрация/обновление SW падали SecurityError'ом.
    "/((?!api/auth|api/health|_next/static|_next/image|sw\\.js$|.*\\.(?:ico|png|svg|jpg|jpeg|gif|webp|webmanifest|pdf)$|robots\\.txt$|sitemap\\.xml$|llms\\.txt$).*)",
  ],
};
