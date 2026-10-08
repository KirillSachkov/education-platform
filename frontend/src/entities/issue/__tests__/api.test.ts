import { describe, it, expect } from "vitest";
import {
  issuesQueryOptions,
  issueDetailQueryOptions,
} from "../api";

describe("issuesQueryOptions", () => {
  it("has correct baseKey", () => {
    expect(issuesQueryOptions.baseKey).toBe("issues");
  });
});

describe("issueDetailQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = issueDetailQueryOptions("issue-1");
    expect(options.queryKey).toEqual(["issues", "issue-1", "detail"]);
  });

  it("is enabled for a valid issueId", () => {
    const options = issueDetailQueryOptions("issue-1");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty issueId", () => {
    const options = issueDetailQueryOptions("");
    expect(options.enabled).toBe(false);
  });

  it("has queryFn defined", () => {
    const options = issueDetailQueryOptions("issue-1");
    expect(options.queryFn).toBeDefined();
  });
});
