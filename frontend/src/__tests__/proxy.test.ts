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

const roles = {
  anonymous: null,
  student: { user: { roles: ["platform-participant"] } },
  author: { user: { roles: ["platform-author"] } },
  admin: { user: { roles: ["platform-admin"] } },
};
const retired = [
  "/catalog",
  "/courses/",
  "/knowledge-base",
  "/collections",
  "/trainer",
  "/trainer/session/1",
  "/admin/trainer",
  "/author/trainer",
  "/author/mock-interviews",
  "/progress",
  "/leaderboard",
  "/level-test/result/1",
  "/roadmaps/1",
  "/certificates/1",
  "/users/1",
  "/courses/devops/roadmap",
  "/courses/devops/progress",
];

describe.each(Object.entries(roles))("route matrix %s", (role, auth) => {
  it.each(retired)("retires %s before route or role checks", async (path) => {
    const response = await proxy({ ...anonymousRequest(path), auth } as never, {} as never);
    expect(response?.headers.get("location")).toBe(
      `https://sachkov-learn.net/${role === "anonymous" ? "pricing" : "home"}`,
    );
  });
  it.each([
    "/courses/devops/learn/m1",
    "/courses/devops/quiz/q1",
    "/courses/devops/issues/i1",
    "/courses/devops/knowledge-base",
    "/courses/devops/collections/c1",
    "/knowledge-base/m1",
    "/collections/c1",
  ])("preserves content and backend entitlement for %s", async (path) => {
    expect(await proxy({ ...anonymousRequest(path), auth } as never, {} as never)).toBeUndefined();
  });
  it.each(["/home", "/saved", "/courses/devops/bookmarks"])(
    "requires sign-in for personal %s",
    async (path) => {
      const response = await proxy({ ...anonymousRequest(path), auth } as never, {} as never);
      if (role === "anonymous")
        expect(response?.headers.get("location")).toContain("/login?callbackUrl=");
      else expect(response).toBeUndefined();
    },
  );
  it("keeps legacy bookmarks pointed at Saved", async () => {
    const response = await proxy({ ...anonymousRequest("/bookmarks"), auth } as never, {} as never);
    expect(response?.headers.get("location")).toBe("https://sachkov-learn.net/saved");
  });
  it("keeps author collection management protected by role", async () => {
    const response = await proxy(
      { ...anonymousRequest("/author/collections"), auth } as never,
      {} as never,
    );
    if (role === "author" || role === "admin") expect(response).toBeUndefined();
    else expect(response?.status).toBe(302);
  });
  it("keeps administration protected by role", async () => {
    const response = await proxy(
      { ...anonymousRequest("/admin/users"), auth } as never,
      {} as never,
    );
    if (role === "admin") expect(response).toBeUndefined();
    else expect(response?.status).toBe(302);
  });
});
