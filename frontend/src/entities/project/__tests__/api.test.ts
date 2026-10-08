import { describe, it, expect } from "vitest";
import {
  projectsQueryOptions,
  projectDetailQueryOptions,
} from "../api";

describe("projectsQueryOptions", () => {
  it("has correct baseKey", () => {
    expect(projectsQueryOptions.baseKey).toBe("projects");
  });
});

describe("projectDetailQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = projectDetailQueryOptions("proj-1");
    expect(options.queryKey).toEqual(["projects", "proj-1", "detail"]);
  });

  it("is enabled for a valid projectId", () => {
    const options = projectDetailQueryOptions("proj-1");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty projectId", () => {
    const options = projectDetailQueryOptions("");
    expect(options.enabled).toBe(false);
  });

  it("has queryFn defined", () => {
    const options = projectDetailQueryOptions("proj-1");
    expect(options.queryFn).toBeDefined();
  });
});
