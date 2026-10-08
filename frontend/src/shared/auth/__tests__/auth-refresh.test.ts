import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("next-auth", () => ({
  default: vi.fn(() => ({
    handlers: {},
    auth: vi.fn(),
    signIn: vi.fn(),
    signOut: vi.fn(),
  })),
}));

import { refreshAccessToken } from "../auth";

function jsonResponse(data: unknown, ok = true, status = 200) {
  return {
    ok,
    status,
    json: vi.fn().mockResolvedValue(data),
  } as unknown as Response;
}

function requestUrl(input: string | URL | Request): string {
  if (typeof input === "string") return input;
  return input instanceof URL ? input.href : input.url;
}

describe("access-token refresh", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    process.env.AUTH_OIDC_ISSUER = "https://auth.test/";
    process.env.AUTH_OIDC_ID = "frontend";
    process.env.AUTH_OIDC_SECRET = "secret";
  });

  it("coalesces concurrent refreshes for the same rotating token", async () => {
    let releaseTokenResponse: (() => void) | undefined;
    const tokenGate = new Promise<void>((resolve) => {
      releaseTokenResponse = resolve;
    });
    const fetchMock = vi.fn(async (input: string | URL | Request, _init?: RequestInit) => {
      const url = requestUrl(input);
      if (url.endsWith("connect/token")) {
        await tokenGate;
        return jsonResponse({
          access_token: "access-next",
          refresh_token: "refresh-next",
          expires_in: 300,
        });
      }
      return jsonResponse({}, false, 503);
    });
    vi.stubGlobal("fetch", fetchMock);

    const first = refreshAccessToken({ refreshToken: "refresh-concurrent", marker: "first" });
    const second = refreshAccessToken({ refreshToken: "refresh-concurrent", marker: "second" });
    releaseTokenResponse?.();

    await expect(first).resolves.toMatchObject({
      refreshToken: "refresh-next",
      accessToken: "access-next",
      marker: "first",
    });
    await expect(second).resolves.toMatchObject({
      refreshToken: "refresh-next",
      accessToken: "access-next",
      marker: "second",
    });
    expect(
      fetchMock.mock.calls.filter(([input]) => requestUrl(input).endsWith("connect/token")),
    ).toHaveLength(1);
  });

  it("reuses a successful rotation while the first caller finishes userinfo", async () => {
    let markUserinfoStarted: (() => void) | undefined;
    const userinfoStarted = new Promise<void>((resolve) => {
      markUserinfoStarted = resolve;
    });
    let releaseUserinfo: (() => void) | undefined;
    const userinfoGate = new Promise<void>((resolve) => {
      releaseUserinfo = resolve;
    });
    const fetchMock = vi.fn(async (input: string | URL | Request, _init?: RequestInit) => {
      const url = requestUrl(input);
      if (url.endsWith("connect/token")) {
        return jsonResponse({
          access_token: "access-next",
          refresh_token: "refresh-next",
          expires_in: 300,
        });
      }
      markUserinfoStarted?.();
      await userinfoGate;
      return jsonResponse({ name: "User" });
    });
    vi.stubGlobal("fetch", fetchMock);

    const first = refreshAccessToken({ refreshToken: "refresh-staggered", marker: "first" });
    await userinfoStarted;
    const second = refreshAccessToken({ refreshToken: "refresh-staggered", marker: "second" });
    releaseUserinfo?.();

    await expect(Promise.all([first, second])).resolves.toHaveLength(2);
    expect(
      fetchMock.mock.calls.filter(([input]) => requestUrl(input).endsWith("connect/token")),
    ).toHaveLength(1);
  });

  it("rejects malformed successful token responses as a dead refresh", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ expires_in: 300 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(refreshAccessToken({ refreshToken: "refresh-malformed" })).resolves.toMatchObject({
      error: "RefreshTokenError",
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("bounds provider requests with an abort signal", async () => {
    let requestSignal: AbortSignal | null | undefined;
    const fetchMock = vi.fn((_input: string | URL | Request, init?: RequestInit) => {
      requestSignal = init?.signal;
      return Promise.resolve(
        jsonResponse({
          access_token: "access-next",
          refresh_token: "refresh-next",
          expires_in: 300,
        }),
      );
    });
    vi.stubGlobal("fetch", fetchMock);

    await refreshAccessToken({ refreshToken: "refresh-signal" });

    expect(requestSignal).toBeInstanceOf(AbortSignal);
  });
});
