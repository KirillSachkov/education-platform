"use client";

// Re-entry guard: SessionGuard, the axios 401 interceptor and the SessionProvider
// refetch poll can all trigger a logout near-simultaneously. Without this the page
// thrashes through repeated signOut + redirect calls (infinite reload) instead of
// leaving cleanly once. Never reset — the page navigates away after logout starts.
let logoutInFlight = false;

export async function fullLogout() {
  await fullLogoutTo("/login");
}

export async function fullLogoutTo(redirectPath: string) {
  if (logoutInFlight) return;
  logoutInFlight = true;

  // 1. Get OpenIddict end_session URL from server (needs idToken from JWT)
  let logoutUrl: string | null = null;
  try {
    const res = await fetch(
      `/api/auth/federated-logout?redirect=${encodeURIComponent(redirectPath)}`,
    );
    const data = await res.json();
    logoutUrl = data.logoutUrl ?? null;
  } catch {
    // Server route failed — will do local-only logout
  }

  // 2. Destroy local NextAuth session (clears cookie)
  const { signOut } = await import("next-auth/react");
  await signOut({ redirect: false });

  // 3. Redirect to OpenIddict end_session (clears Identity cookie)
  //    or fallback to redirectPath if no OIDC URL available
  window.location.href = logoutUrl ?? redirectPath;
}

/**
 * Logout for a DEAD session (refresh token invalid → `RefreshTokenError`).
 *
 * Skips the OpenIddict end_session round-trip: the `id_token_hint` is encrypted
 * with a key the server no longer trusts (e.g. after the dev signing cert was
 * regenerated on a container rebuild), so end_session can't validate the
 * `post_logout_redirect_uri` and bounces the browser back to an authenticated
 * page → infinite reload. Instead we clear the local NextAuth cookie and
 * hard-replace to `/login`, a public route outside the `(app)` auth machinery
 * (no SessionProvider/SessionGuard → no refresh retries). Pairs with the login
 * page tolerating errored sessions so it does not redirect the dead session back.
 */
export async function localLogoutToLogin() {
  if (logoutInFlight) return;
  logoutInFlight = true;

  try {
    const { signOut } = await import("next-auth/react");
    await signOut({ redirect: false });
  } catch {
    // ignore — we redirect regardless
  }

  window.location.replace("/login");
}
