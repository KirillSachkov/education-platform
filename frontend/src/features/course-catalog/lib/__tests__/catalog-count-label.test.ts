import { describe, expect, it } from "vitest";
import { catalogCountLabel } from "../catalog-count-label";

describe("catalogCountLabel", () => {
  it("uses neutral «программ» for the all-tab (mixed course/intensive/marathon)", () => {
    expect(catalogCountLabel(7, "all")).toBe("7 программ");
    expect(catalogCountLabel(1, "all")).toBe("1 программа");
    expect(catalogCountLabel(2, "all")).toBe("2 программы");
    expect(catalogCountLabel(0, "all")).toBe("0 программ");
  });

  it("pluralizes «курс» for the COURSE tab", () => {
    expect(catalogCountLabel(1, "COURSE")).toBe("1 курс");
    expect(catalogCountLabel(4, "COURSE")).toBe("4 курса");
    expect(catalogCountLabel(7, "COURSE")).toBe("7 курсов");
    expect(catalogCountLabel(0, "COURSE")).toBe("0 курсов");
  });

  it("pluralizes «интенсив» for the INTENSIVE tab", () => {
    expect(catalogCountLabel(1, "INTENSIVE")).toBe("1 интенсив");
    expect(catalogCountLabel(2, "INTENSIVE")).toBe("2 интенсива");
    expect(catalogCountLabel(5, "INTENSIVE")).toBe("5 интенсивов");
  });

  it("pluralizes «марафон» for the MARATHON tab", () => {
    expect(catalogCountLabel(1, "MARATHON")).toBe("1 марафон");
    expect(catalogCountLabel(3, "MARATHON")).toBe("3 марафона");
    expect(catalogCountLabel(11, "MARATHON")).toBe("11 марафонов");
  });
});
