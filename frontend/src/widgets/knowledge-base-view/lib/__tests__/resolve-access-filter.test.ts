import { describe, expect, it } from "vitest";
import { resolveKnowledgeBaseAccessFilter } from "../resolve-access-filter";

describe("resolveKnowledgeBaseAccessFilter", () => {
  it("maps the user-facing free-only toggle to strict anonymous PUBLIC access", () => {
    expect(resolveKnowledgeBaseAccessFilter(true)).toBe("public");
  });

  it("leaves the unfiltered knowledge base unchanged", () => {
    expect(resolveKnowledgeBaseAccessFilter(false)).toBeUndefined();
  });
});
