import NextAuth, { type DefaultSession } from "next-auth";

const DEFAULT_REDIRECT_PATH = "/home";

// ---------- Type augmentation ----------

declare module "next-auth" {
  interface Session {
    accessToken?: string;
    error?: "RefreshTokenError";
    user: {
      id?: string;
      username?: string;
      displayName?: string;
      roles?: string[];
    } & DefaultSession["user"];
  }
}

declare module "@auth/core/jwt" {
  interface JWT {
    accessToken?: string;
    refreshToken?: string;
    expiresAt?: number;
    username?: string;
    displayName?: string;
    roles?: string[];
    error?: "RefreshTokenError";
  }
}

// ---------- Configuration ----------

export const { handlers, auth, signIn, signOut } = NextAuth({
  debug: process.env.NODE_ENV === "development",
  providers: [
    {
      id: "auth-service",
      name: "AuthService",
      type: "oidc",
      // issuer for iss-parameter validation (matches the fixed OIDC issuer)
      issuer: `${process.env.NEXT_PUBLIC_AUTH_ORIGIN}/`,
      // discovery URL — may differ from issuer in Docker (http://nginx/ vs http://localhost/)
      wellKnown: `${process.env.AUTH_OIDC_ISSUER}.well-known/openid-configuration`,
      clientId: process.env.AUTH_OIDC_ID!,
      clientSecret: process.env.AUTH_OIDC_SECRET!,
      authorization: {
        url: `${process.env.NEXT_PUBLIC_AUTH_ORIGIN}/connect/authorize`,
        params: {
          scope: "openid profile email roles offline_access platform",
        },
      },
      // Explicit server-side endpoints — prevents Auth.js from re-fetching
      // discovery using issuer URL (unreachable from inside Docker container)
      token: `${process.env.AUTH_OIDC_ISSUER}connect/token`,
      userinfo: `${process.env.AUTH_OIDC_ISSUER}connect/userinfo`,
    },
  ],

  trustHost: true,
  session: {
    strategy: "jwt",
    // Выровнено с OpenIddict refresh token lifetime (180 дней).
    // Если refresh ещё валиден — session должна быть живой, чтобы не терять контекст.
    maxAge: 60 * 60 * 24 * 180,
  },

  callbacks: {
    async redirect({ url, baseUrl }) {
      if (url.startsWith("/")) {
        return `${baseUrl}${url}`;
      }

      try {
        const target = new URL(url);
        if (target.origin === baseUrl) {
          return url;
        }
      } catch {
        // no-op
      }

      return `${baseUrl}${DEFAULT_REDIRECT_PATH}`;
    },

    async jwt({ token, account, profile, trigger }) {
      if (account) {
        token.accessToken = account.access_token;
        token.refreshToken = account.refresh_token;
        token.expiresAt = account.expires_at;

        if (profile) {
          const p = profile as Record<string, unknown>;
          const rawRoles = p.roles;
          token.roles = Array.isArray(rawRoles)
            ? rawRoles
            : typeof rawRoles === "string"
              ? [rawRoles]
              : undefined;
          token.username = p.preferred_username as string | undefined;
          // Empty string ("") = backend confirmed user has no DisplayName → redirect to onboarding.
          // undefined = claim absent (legacy session pre-rollout) → don't force onboarding.
          token.displayName = typeof p.display_name === "string" ? p.display_name : undefined;
        }

        // Ensure expiresAt is set (OpenIddict may not include expires_at)
        if (!token.expiresAt && account.expires_in) {
          token.expiresAt = Math.floor(Date.now() / 1000) + account.expires_in;
        }

        return token;
      }

      // useSession().update() — клиент просит свежие claims (после CompleteProfile и т.п.).
      // Гоним через refresh_token → /connect/userinfo, чтобы подтянуть новый display_name.
      if (trigger === "update" && token.refreshToken) {
        return await refreshAccessToken(token);
      }

      // Access token lifetime = 5 min (backend). Refresh за 1 мин до истечения,
      // чтобы иметь запас на медленные сети, но не делать refresh на каждый запрос.
      const REFRESH_BUFFER_MS = 60 * 1000;
      if (!token.expiresAt || Date.now() < token.expiresAt * 1000 - REFRESH_BUFFER_MS) {
        return token;
      }

      // Token expired — try refresh
      if (token.refreshToken) {
        return await refreshAccessToken(token);
      }

      return token;
    },

    async session({ session, token }) {
      session.accessToken = token.accessToken;
      session.error = token.error;

      if (token.sub) {
        session.user.id = token.sub;
      }

      session.user.username = token.username;
      session.user.displayName = token.displayName;
      session.user.roles = token.roles;

      return session;
    },
  },

  pages: {
    signIn: "/login",
  },
});

// ---------- Token refresh ----------

export interface RefreshableToken {
  refreshToken?: string;
  accessToken?: string;
  expiresAt?: number;
  roles?: string[];
  error?: "RefreshTokenError";
  [key: string]: unknown;
}

// invalid_grant / invalid_token = refresh_token реально невалиден — ретрай не спасёт.
// Остальное (network, 5xx) — пробуем ещё раз.
const FATAL_OAUTH_ERRORS = new Set(["invalid_grant", "invalid_token"]);
const REFRESH_MAX_ATTEMPTS = 3;
const REFRESH_BACKOFFS_MS = [0, 250, 1000];
const REFRESH_REQUEST_TIMEOUT_MS = 5000;
const USERINFO_REQUEST_TIMEOUT_MS = 3000;
// Auth.js writes the rotated token cookie only after the JWT callback finishes.
// Keep a successful rotation briefly so requests that arrived with the old cookie
// during userinfo/cookie serialization cannot spend the one-time token twice.
const REFRESH_SUCCESS_GRACE_MS = 10_000;

interface TokenEndpointResponse {
  access_token: string;
  refresh_token?: string;
  expires_in: number;
}

const refreshRequests = new Map<string, Promise<TokenEndpointResponse>>();

class FatalRefreshError extends Error {
  constructor(public oauthError: string) {
    super(oauthError);
  }
}

async function tryRefreshOnce(refreshToken: string): Promise<TokenEndpointResponse> {
  const tokenUrl = `${process.env.AUTH_OIDC_ISSUER}connect/token`;

  const response = await fetch(tokenUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "refresh_token",
      client_id: process.env.AUTH_OIDC_ID ?? "",
      client_secret: process.env.AUTH_OIDC_SECRET ?? "",
      refresh_token: refreshToken,
    }),
    signal: AbortSignal.timeout(REFRESH_REQUEST_TIMEOUT_MS),
  });

  const data: unknown = await response.json().catch(() => ({}));

  if (!response.ok) {
    const oauthError = isRecord(data) && typeof data.error === "string" ? data.error : "";
    if (FATAL_OAUTH_ERRORS.has(oauthError)) {
      throw new FatalRefreshError(oauthError);
    }
    throw new Error(oauthError || `Token refresh failed (${response.status})`);
  }

  if (
    !isRecord(data) ||
    typeof data.access_token !== "string" ||
    data.access_token.length === 0 ||
    typeof data.expires_in !== "number" ||
    !Number.isFinite(data.expires_in) ||
    data.expires_in <= 0 ||
    (data.refresh_token !== undefined && typeof data.refresh_token !== "string")
  ) {
    throw new FatalRefreshError("invalid_token_response");
  }

  return {
    access_token: data.access_token,
    refresh_token: data.refresh_token,
    expires_in: data.expires_in,
  };
}

async function refreshWithRetry(refreshToken: string): Promise<TokenEndpointResponse> {
  let lastError: unknown = null;

  for (let attempt = 0; attempt < REFRESH_MAX_ATTEMPTS; attempt++) {
    const backoffMs = REFRESH_BACKOFFS_MS[attempt] ?? 0;
    if (backoffMs > 0) {
      await new Promise((r) => setTimeout(r, backoffMs));
    }

    try {
      return await tryRefreshOnce(refreshToken);
    } catch (err) {
      lastError = err;
      if (err instanceof FatalRefreshError) {
        throw err; // Don't retry — refresh is definitively dead
      }
    }
  }

  throw lastError instanceof Error ? lastError : new Error("Token refresh failed");
}

function getRefreshedProviderToken(refreshToken: string): Promise<TokenEndpointResponse> {
  const existing = refreshRequests.get(refreshToken);
  if (existing) return existing;

  const request = refreshWithRetry(refreshToken);
  refreshRequests.set(refreshToken, request);
  const cleanup = () => {
    if (refreshRequests.get(refreshToken) === request) {
      refreshRequests.delete(refreshToken);
    }
  };
  void request.then(() => {
    const timeout = setTimeout(cleanup, REFRESH_SUCCESS_GRACE_MS);
    timeout.unref();
  }, cleanup);
  return request;
}

export async function refreshAccessToken(token: RefreshableToken): Promise<RefreshableToken> {
  if (!token.refreshToken) {
    return { ...token, error: "RefreshTokenError" };
  }

  let data: TokenEndpointResponse;
  try {
    data = await getRefreshedProviderToken(token.refreshToken);
  } catch (error) {
    console.warn("[auth] refresh failed:", error);
    return { ...token, error: "RefreshTokenError" };
  }

  const refreshedToken: RefreshableToken = {
    ...token,
    accessToken: data.access_token,
    refreshToken: data.refresh_token ?? token.refreshToken,
    expiresAt: Math.floor(Date.now() / 1000) + data.expires_in,
    error: undefined,
  };

  try {
    const userinfoUrl = `${process.env.AUTH_OIDC_ISSUER}connect/userinfo`;
    const userinfoRes = await fetch(userinfoUrl, {
      headers: { Authorization: `Bearer ${data.access_token}` },
      signal: AbortSignal.timeout(USERINFO_REQUEST_TIMEOUT_MS),
    });

    if (userinfoRes.ok) {
      const userinfo = (await userinfoRes.json()) as Record<string, unknown>;
      refreshedToken.name = userinfo.name as string;
      refreshedToken.username = userinfo.preferred_username as string;
      refreshedToken.displayName =
        typeof userinfo.display_name === "string" ? userinfo.display_name : undefined;
    }
  } catch {
    // Graceful degradation — claims stay as they were
  }

  return refreshedToken;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
