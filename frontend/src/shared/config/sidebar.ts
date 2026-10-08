/**
 * Sidebar state constants — shared by the client `SidebarProvider`
 * (`shared/ui/kit/sidebar.tsx`) and the server-side cookie reader
 * (`shared/lib/sidebar-server.ts`). Kept framework-free so both the client
 * bundle and server components can import it.
 */

/** Cookie persisting the user's wide-screen sidebar preference (shadcn convention). */
export const SIDEBAR_COOKIE_NAME = "sidebar_state";

/** 7 days — long enough to feel sticky, short enough to not be forever-stale. */
export const SIDEBAR_COOKIE_MAX_AGE = 60 * 60 * 24 * 7;

/**
 * Desktop viewport width (px) below which the sidebar auto-collapses to its
 * icon rail. Deliberately a "last resort" threshold — only small laptops and
 * landscape tablets fall below it; on anything wider the user's explicit
 * choice is always respected. Mobile (<768px) uses the Sheet instead and is
 * unaffected by this value.
 *
 * Consolidates the previous per-sidebar breakpoints (App/Builder/Trainer 1100px,
 * Course 1160px, provider compact 1280px) into one — so all sidebars collapse
 * at the same width and there's no in-between range where they disagree.
 */
export const SIDEBAR_AUTOCOLLAPSE_BREAKPOINT = 1024;
