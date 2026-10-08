import { describe, expect, it } from "vitest";
import {
  buildSearchTitleFallbackHighlight,
  pickSearchBodyHighlights,
  pickSearchTitleHighlight,
} from "../search-highlights";

describe("search highlight helpers", () => {
  it("prefers title highlight for the title line", () => {
    expect(
      pickSearchTitleHighlight([
        { field: "description", snippet: "description snippet" },
        { field: "title", snippet: "how to <mark>search</mark>" },
      ]),
    ).toBe("how to <mark>search</mark>");
  });

  it("returns description snippets without duplicates", () => {
    expect(
      pickSearchBodyHighlights([
        { field: "description", snippet: "description snippet" },
        { field: "description", snippet: "same snippet" },
        { field: "description", snippet: "same snippet" },
      ]),
    ).toEqual(["description snippet", "same snippet"]);
  });

  it("builds a case-insensitive fallback title highlight for cyrillic text", () => {
    expect(buildSearchTitleFallbackHighlight("Как проходить курс", "как")).toBe(
      "<mark>Как</mark> проходить курс",
    );
  });

  it("preserves the original title casing inside the highlight", () => {
    expect(buildSearchTitleFallbackHighlight("КАК проходить курс", "как")).toBe(
      "<mark>КАК</mark> проходить курс",
    );
  });
});
