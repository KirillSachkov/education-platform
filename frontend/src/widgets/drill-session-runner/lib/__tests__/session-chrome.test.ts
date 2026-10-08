import { describe, expect, it } from "vitest";
import { resolveSessionChrome } from "../session-chrome";

describe("resolveSessionChrome", () => {
  it("LEARN training-pack default → «Тренировка» + sub=learn back (unchanged)", () => {
    const r = resolveSessionChrome({ mode: "LEARN", topicIds: ["t1"] });
    expect(r.modeLabel).toBe("Тренировка");
    expect(r.backHref).toBe("/trainer?tab=study&topic=t1&sub=learn");
  });

  // #656: клик по вопросу из списка / SRS / закладки — на бэке LEARN, но это «тест»,
  // а не пачка-тренировка: явные override-ы из точки запуска побеждают вывод из mode.
  it("review session → override label «Тест» + explicit back win over LEARN defaults", () => {
    const r = resolveSessionChrome({
      mode: "LEARN",
      topicIds: ["t1"],
      backHref: "/trainer?tab=study&topic=t1&sub=list",
      modeLabelOverride: "Тест",
    });
    expect(r.modeLabel).toBe("Тест");
    expect(r.backHref).toBe("/trainer?tab=study&topic=t1&sub=list");
  });

  it("DRILL → «Тест» + sub=test back", () => {
    const r = resolveSessionChrome({ mode: "DRILL", topicIds: ["t1"] });
    expect(r.modeLabel).toBe("Тест");
    expect(r.backHref).toBe("/trainer?tab=study&topic=t1&sub=test");
  });

  it("MOCK → «Симуляция» + ?tab=mock back", () => {
    const r = resolveSessionChrome({ mode: "MOCK", topicIds: ["t1"] });
    expect(r.modeLabel).toBe("Симуляция");
    expect(r.backHref).toBe("/trainer?tab=mock");
  });

  it("no topic → trainer landing back", () => {
    const r = resolveSessionChrome({ mode: "LEARN", topicIds: [] });
    expect(r.backHref).toBe("/trainer");
  });
});
