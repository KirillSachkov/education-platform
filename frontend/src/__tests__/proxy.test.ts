import { describe, expect, it, vi } from "vitest";

vi.mock("@/shared/auth/auth", () => ({
  auth: (handler: unknown) => handler,
}));

import { proxy } from "@/proxy";

function anonymousRequest(pathname: string) {
  return {
    auth: null,
    nextUrl: new URL(pathname, "https://sachkov-learn.net"),
  };
}

describe.each(["/c-sharp", "/dotnet", "/asp-net-core"])("SEO route %s", (pathname) => {
  it("is public for anonymous visitors", async () => {
    const response = await proxy(anonymousRequest(pathname) as never, {} as never);

    expect(response).toBeUndefined();
  });
});

it("keeps the AI discovery manifest public", async () => {
  const response = await proxy(anonymousRequest("/llms.txt") as never, {} as never);

  expect(response).toBeUndefined();
});

it("keeps the Yandex Webmaster verification file public", async () => {
  const response = await proxy(
    anonymousRequest("/yandex_3d24ffdd8bed9484.html") as never,
    {} as never,
  );

  expect(response).toBeUndefined();
});

it("keeps unrelated HTML paths protected", async () => {
  const response = await proxy(anonymousRequest("/verification.html") as never, {} as never);

  expect(response).toBeInstanceOf(Response);
  expect(response?.status).toBe(302);
  expect(response?.headers.get("location")).toBe(
    "https://sachkov-learn.net/login?callbackUrl=%2Fverification.html",
  );
});

it("keeps nested paths outside the SEO allowlist protected", async () => {
  const response = await proxy(anonymousRequest("/dotnet/private") as never, {} as never);

  expect(response).toBeInstanceOf(Response);
  expect(response?.status).toBe(302);
  expect(response?.headers.get("location")).toBe(
    "https://sachkov-learn.net/login?callbackUrl=%2Fdotnet%2Fprivate",
  );
});
