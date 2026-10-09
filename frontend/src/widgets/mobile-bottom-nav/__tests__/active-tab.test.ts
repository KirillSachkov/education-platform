import { describe, expect, it } from "vitest";
import { BOTTOM_NAV_TABS, resolveActiveTabIndex } from "../lib/active-tab";
const active = (path: string) => BOTTOM_NAV_TABS[resolveActiveTabIndex(path)]?.id;

describe("learning navigation", () => {
  it.each([
    "/home",
    "/courses/devops/learn/m1",
    "/courses/devops/quiz/q1",
    "/courses/devops/collections/c1",
    "/knowledge-base/m1",
    "/collections/c1",
  ])("keeps purchased content %s in learning", (path) => {
    expect(active(path)).toBe("home");
  });
  it("keeps saved content reachable", () => {
    expect(active("/saved")).toBe("saved");
  });
  it.each(["/admin/users", "/author/collections", "/profile", "/settings", "/pricing"])(
    "keeps account and teaching %s in the menu",
    (path) => {
      expect(active(path)).toBe("menu");
    },
  );
  it.each(["/trainer", "/knowledge-base", "/courses", "/leaderboard", "/level-test", "/login"])(
    "does not promote retired or external %s",
    (path) => {
      expect(resolveActiveTabIndex(path)).toBe(-1);
    },
  );
  it("uses Menu as the only action", () => {
    expect(BOTTOM_NAV_TABS.filter((tab) => !tab.href).map((tab) => tab.id)).toEqual(["menu"]);
  });
});
