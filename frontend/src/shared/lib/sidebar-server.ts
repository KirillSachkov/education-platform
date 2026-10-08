import { cookies } from "next/headers";

import { SIDEBAR_COOKIE_NAME } from "@/shared/config/sidebar";

/**
 * Reads the persisted sidebar open/collapsed preference from the request
 * cookie, server-side. Passed into `AppLayout` → `SidebarProvider` as
 * `defaultOpen` so the initial server render matches the client state and the
 * choice survives navigation between route-group layouts (each of which mounts
 * its own provider). Defaults to open when no cookie is set.
 *
 * Server-only: imports `next/headers`. Call from Server Component layouts.
 */
export async function readSidebarDefaultOpen(): Promise<boolean> {
  const value = (await cookies()).get(SIDEBAR_COOKIE_NAME)?.value;
  return value !== "false";
}
