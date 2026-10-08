import { type NextRequest, NextResponse } from "next/server";
import { getToken } from "next-auth/jwt";

const ALLOWED_LOGOUT_REDIRECTS = new Set(["/", "/login"]);
const REVOCATION_TIMEOUT_MS = 2000;

async function revokeRefreshToken(refreshToken: string): Promise<void> {
  const issuer = process.env.AUTH_OIDC_ISSUER;
  const clientId = process.env.AUTH_OIDC_ID;
  const clientSecret = process.env.AUTH_OIDC_SECRET;

  if (!issuer || !clientId || !clientSecret) return;

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), REVOCATION_TIMEOUT_MS);

  try {
    await fetch(`${issuer}connect/revocation`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        token: refreshToken,
        token_type_hint: "refresh_token",
      }),
      signal: controller.signal,
    });
  } catch (err) {
    console.warn("[federated-logout] revocation failed:", err);
  } finally {
    clearTimeout(timer);
  }
}

export async function GET(request: NextRequest) {
  const redirectPath =
    request.nextUrl.searchParams.get("redirect") || "/login";
  const safeRedirect = ALLOWED_LOGOUT_REDIRECTS.has(redirectPath)
    ? redirectPath
    : "/login";

  const token = await getToken({
    req: request,
    secret: process.env.AUTH_SECRET,
  });

  if (token?.refreshToken && typeof token.refreshToken === "string") {
    await revokeRefreshToken(token.refreshToken);
  }

  const authOrigin = process.env.NEXT_PUBLIC_AUTH_ORIGIN;

  if (authOrigin) {
    try {
      const postLogoutUri = `${authOrigin}${safeRedirect}`;
      const endSessionUrl = new URL(`${authOrigin}/connect/logout`);
      endSessionUrl.searchParams.set(
        "post_logout_redirect_uri",
        postLogoutUri,
      );

      return NextResponse.json({ logoutUrl: endSessionUrl.toString() });
    } catch {
      // Invalid URL — fallback
    }
  }

  return NextResponse.json({ logoutUrl: null });
}
