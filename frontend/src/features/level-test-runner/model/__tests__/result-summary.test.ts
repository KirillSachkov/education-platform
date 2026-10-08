import { describe, expect, it } from "vitest";
import { resolveRecommendationCta, resolveWeakestSectionTitles } from "../result-summary";

describe("resolveRecommendationCta", () => {
  it("links to the course when recommendedCourseId resolved to a slug", () => {
    expect(
      resolveRecommendationCta({ recommendedCourseId: "course-1", courseSlug: "dotnet-fullstack" }),
    ).toEqual({ kind: "course", href: "/courses/dotnet-fullstack" });
  });

  it("falls back to the catalog when there is no recommendation", () => {
    expect(resolveRecommendationCta({ recommendedCourseId: null, courseSlug: null })).toEqual({
      kind: "catalog",
      href: "/courses",
    });
  });

  it("falls back to the catalog when the course failed to resolve", () => {
    expect(
      resolveRecommendationCta({ recommendedCourseId: "course-1", courseSlug: null }),
    ).toEqual({ kind: "catalog", href: "/courses" });
  });
});

describe("resolveWeakestSectionTitles", () => {
  const sections = [
    { key: "linq", title: "LINQ и коллекции" },
    { key: "async", title: "Асинхронность" },
  ];

  it("maps weakest keys to section titles preserving order", () => {
    expect(resolveWeakestSectionTitles(sections, ["async", "linq"])).toEqual([
      "Асинхронность",
      "LINQ и коллекции",
    ]);
  });

  it("falls back to the raw key when the section snapshot is missing it", () => {
    expect(resolveWeakestSectionTitles(sections, ["unknown-key"])).toEqual(["unknown-key"]);
  });

  it("returns empty list for no weakest keys", () => {
    expect(resolveWeakestSectionTitles(sections, [])).toEqual([]);
  });
});
