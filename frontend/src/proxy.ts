import { auth } from "@/shared/auth/auth";
import { legacyRedirect } from "@/shared/config/legacy-redirect";
import { ROLES } from "@/shared/auth/roles";

/** Public learning content keeps backend per-item entitlement checks. */
const publicRoutes = [
  "/",
  "/login",
  "/legal",
  "/pricing",
  "/c-sharp",
  "/dotnet",
  "/asp-net-core",
  "/offline",
  "/llms.txt",
  "/yandex_3d24ffdd8bed9484.html",
];
const publicPrefixes = [
  "/login/",
  "/knowledge-base/",
  "/invite/",
  "/legal/",
  "/courses/",
  "/collections/",
  "/pricing/",
];
const authRequiredPatterns = [/^\/courses\/[^/]+\/bookmarks(?:\/|$)/];

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

  const destination = legacyRedirect(pathname, isLoggedIn);
  if (destination) return Response.redirect(new URL(destination, req.nextUrl.origin));

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
