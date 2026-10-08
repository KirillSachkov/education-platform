import { useSyncExternalStore } from "react";

import { SIDEBAR_AUTOCOLLAPSE_BREAKPOINT } from "@/shared/config/sidebar";

function subscribe(callback: () => void) {
  const mql = window.matchMedia(`(max-width: ${SIDEBAR_AUTOCOLLAPSE_BREAKPOINT - 1}px)`);
  mql.addEventListener("change", callback);
  return () => mql.removeEventListener("change", callback);
}

function getSnapshot() {
  return window.innerWidth < SIDEBAR_AUTOCOLLAPSE_BREAKPOINT;
}

function getServerSnapshot() {
  return false;
}

/**
 * Returns true when the viewport is narrower than the sidebar auto-collapse
 * breakpoint ({@link SIDEBAR_AUTOCOLLAPSE_BREAKPOINT}). The `SidebarProvider`
 * uses it (combined with `!isMobile`) to force the desktop sidebar into its
 * icon rail only when the screen is genuinely too narrow.
 */
export function useIsNarrowViewport() {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}
