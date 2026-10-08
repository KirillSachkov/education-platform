import { describe, it, expect } from "vitest";
import {
  modulesQueryOptions,
  moduleDetailQueryOptions,
  moduleOverviewQueryOptions,
} from "../api";

describe("modulesQueryOptions", () => {
  it("has correct baseKey", () => {
    expect(modulesQueryOptions.baseKey).toBe("modules");
  });
});

describe("moduleDetailQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = moduleDetailQueryOptions("mod-1");
    expect(options.queryKey).toEqual(["modules", "mod-1", "detail"]);
  });

  it("is enabled for a valid moduleId", () => {
    const options = moduleDetailQueryOptions("mod-1");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty moduleId", () => {
    const options = moduleDetailQueryOptions("");
    expect(options.enabled).toBe(false);
  });

  it("has queryFn defined", () => {
    const options = moduleDetailQueryOptions("mod-1");
    expect(options.queryFn).toBeDefined();
  });
});

describe("moduleOverviewQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = moduleOverviewQueryOptions("mod-2");
    expect(options.queryKey).toEqual(["modules", "mod-2", "overview"]);
  });

  it("is enabled for a valid moduleId", () => {
    const options = moduleOverviewQueryOptions("mod-2");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty moduleId", () => {
    const options = moduleOverviewQueryOptions("");
    expect(options.enabled).toBe(false);
  });
});
