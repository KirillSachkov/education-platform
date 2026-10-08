import { describe, expect, it } from "vitest";
import { resolveKinescopePosterUrl } from "../kinescope-interactive-player";

describe("resolveKinescopePosterUrl", () => {
  it("resolves app-relative file URLs against the app origin", () => {
    expect(
      resolveKinescopePosterUrl(
        "/api/files/019e20fd-2971-748b-8a95-338066f921a8/content",
        "https://platform.example/@sachkov/courses/dotnet/learn/material",
      ),
    ).toBe(
      "https://platform.example/api/files/019e20fd-2971-748b-8a95-338066f921a8/content",
    );
  });

  it("keeps absolute poster URLs unchanged", () => {
    expect(
      resolveKinescopePosterUrl(
        "https://cdn.kinescope.io/poster.jpg",
        "https://platform.example/",
      ),
    ).toBe("https://cdn.kinescope.io/poster.jpg");
  });
});
