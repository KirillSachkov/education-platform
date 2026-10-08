import { describe, expect, it } from "vitest";
import { BOTTOM_NAV_TABS, resolveActiveTabIndex } from "../lib/active-tab";

const tabIndex = Object.fromEntries(
  BOTTOM_NAV_TABS.map((tab, i) => [tab.id, i] as const),
) as Record<(typeof BOTTOM_NAV_TABS)[number]["id"], number>;

describe("BOTTOM_NAV_TABS layout", () => {
  it("has exactly 5 tabs with Главная in the center", () => {
    expect(BOTTOM_NAV_TABS).toHaveLength(5);
    expect(BOTTOM_NAV_TABS[2].id).toBe("home");
  });

  it("only «Меню» is an action tab (href === null)", () => {
    expect(BOTTOM_NAV_TABS.filter((tab) => tab.href === null).map((tab) => tab.id)).toEqual([
      "menu",
    ]);
  });
});

describe("resolveActiveTabIndex", () => {
  it("matches /home and its nested routes", () => {
    expect(resolveActiveTabIndex("/home")).toBe(tabIndex.home);
    expect(resolveActiveTabIndex("/home/something")).toBe(tabIndex.home);
  });

  it("matches /courses and legacy @slug course sub-routes", () => {
    expect(resolveActiveTabIndex("/courses")).toBe(tabIndex.courses);
    expect(resolveActiveTabIndex("/@sachkov/courses")).toBe(tabIndex.courses);
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet")).toBe(tabIndex.courses);
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet/learn/m1")).toBe(tabIndex.courses);
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet/program")).toBe(tabIndex.courses);
  });

  it("matches both standalone and course-scoped knowledge base", () => {
    expect(resolveActiveTabIndex("/knowledge-base")).toBe(tabIndex["knowledge-base"]);
    expect(resolveActiveTabIndex("/knowledge-base/m-1")).toBe(tabIndex["knowledge-base"]);
    expect(resolveActiveTabIndex("/@sachkov/knowledge-base")).toBe(tabIndex["knowledge-base"]);
    expect(resolveActiveTabIndex("/@sachkov/collections")).toBe(tabIndex["knowledge-base"]);
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet/knowledge-base")).toBe(
      tabIndex["knowledge-base"],
    );
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet/bookmarks")).toBe(
      tabIndex["knowledge-base"],
    );
  });

  it("matches /saved and its nested routes → Закладки tab", () => {
    expect(resolveActiveTabIndex("/saved")).toBe(tabIndex.saved);
    expect(resolveActiveTabIndex("/saved/")).toBe(tabIndex.saved);
  });

  it("matches access surfaces (pricing / my-plans / payments) → Меню tab", () => {
    expect(resolveActiveTabIndex("/pricing")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/pricing/full-access")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/my-plans")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/payments")).toBe(tabIndex.menu);
  });

  it("matches profile + settings + author + admin + users → Меню tab", () => {
    expect(resolveActiveTabIndex("/profile")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/settings")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/settings/account")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/author/courses")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/admin/users")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/users/123")).toBe(tabIndex.menu);
  });

  it("matches notifications / progress / leaderboard → Меню tab (sheet destinations)", () => {
    expect(resolveActiveTabIndex("/notifications")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/progress")).toBe(tabIndex.menu);
    expect(resolveActiveTabIndex("/leaderboard")).toBe(tabIndex.menu);
  });

  it("matches legacy bare @slug landing as Home", () => {
    // Legacy single-tenant URLs: `/@slug`, `/@slug/`, `/@slug/home` are home surfaces.
    expect(resolveActiveTabIndex("/@sachkov")).toBe(tabIndex.home);
    expect(resolveActiveTabIndex("/@sachkov/")).toBe(tabIndex.home);
    expect(resolveActiveTabIndex("/@sachkov/home")).toBe(tabIndex.home);
  });

  it("returns -1 for routes outside the bottom-nav surface", () => {
    expect(resolveActiveTabIndex("/login")).toBe(-1);
    expect(resolveActiveTabIndex("/onboarding/profile")).toBe(-1);
    expect(resolveActiveTabIndex("/legal/terms")).toBe(-1);
  });

  it("courses tab takes priority over menu for course-scoped author routes", () => {
    // Regression guard — `/@author/courses/X` must light up Каталог, not Меню,
    // even though the menu predicate also accepts /author/* paths (without @).
    expect(resolveActiveTabIndex("/@sachkov/courses/dotnet")).toBe(tabIndex.courses);
  });

  it("knowledge-base tab takes priority over menu for /@author/knowledge-base", () => {
    expect(resolveActiveTabIndex("/@sachkov/knowledge-base/m-1")).toBe(
      tabIndex["knowledge-base"],
    );
  });
});
