import { routes } from "./routes";

const retiredSections = [
  /^\/(?:catalog|courses|knowledge-base|collections)\/?$/,
  /^\/(?:trainer|leaderboard|progress|level-test|roadmaps|certificates|users)(?:\/|$)/,
  /^\/courses\/[^/]+\/(?:roadmap|progress)(?:\/|$)/,
  /^\/author\/(?:trainer|mock-interviews|level-test|roadmaps)(?:\/|$)/,
  /^\/author\/courses\/[^/]+\/roadmap(?:\/|$)/,
  /^\/admin\/(?:trainer|level-test)(?:\/|$)/,
];

/** Retire product surfaces while keeping concrete purchased content links valid. */
export function legacyRedirect(pathname: string, isLoggedIn: boolean): string | null {
  if (/^\/bookmarks\/?$/.test(pathname)) return routes.saved;
  return retiredSections.some((pattern) => pattern.test(pathname))
    ? isLoggedIn
      ? routes.home
      : routes.pricing
    : null;
}
