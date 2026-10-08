import { describe, it, expect } from "vitest";
import {
  coursesQueryOptions,
  courseDetailQueryOptions,
  courseLandingQueryOptions,
  courseBuilderQueryOptions,
  courseCurriculumQueryOptions,
} from "../api";

describe("coursesQueryOptions", () => {
  it("has correct baseKey", () => {
    expect(coursesQueryOptions.baseKey).toBe("courses");
  });

  it("getMyCoursesInfiniteOptions includes baseKey in queryKey", () => {
    const options = coursesQueryOptions.getMyCoursesInfiniteOptions({
      limit: 10,
    });
    expect(options.queryKey[0]).toBe("courses");
    expect(options.queryKey[1]).toBe("my");
    expect(options.queryKey[2]).toEqual({ limit: 10 });
  });
});

describe("courseDetailQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = courseDetailQueryOptions("course-1");
    expect(options.queryKey).toEqual(["courses", "course-1", "detail"]);
  });

  it("has queryFn defined", () => {
    const options = courseDetailQueryOptions("course-1");
    expect(options.queryFn).toBeDefined();
  });
});

describe("courseLandingQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = courseLandingQueryOptions("course-1");
    expect(options.queryKey).toEqual(["courses", "course-1", "landing"]);
  });

  it("is enabled for a valid courseId", () => {
    const options = courseLandingQueryOptions("course-1");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty courseId", () => {
    const options = courseLandingQueryOptions("");
    expect(options.enabled).toBe(false);
  });
});

describe("courseBuilderQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = courseBuilderQueryOptions("course-2");
    expect(options.queryKey).toEqual(["courses", "course-2", "builder"]);
  });

  it("is enabled for a valid courseId", () => {
    const options = courseBuilderQueryOptions("course-2");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty courseId", () => {
    const options = courseBuilderQueryOptions("");
    expect(options.enabled).toBe(false);
  });
});

describe("courseCurriculumQueryOptions", () => {
  it("returns correct queryKey", () => {
    const options = courseCurriculumQueryOptions("course-3");
    expect(options.queryKey).toEqual(["courses", "course-3", "curriculum"]);
  });

  it("is enabled for a valid courseId", () => {
    const options = courseCurriculumQueryOptions("course-3");
    expect(options.enabled).toBe(true);
  });

  it("is disabled for an empty courseId", () => {
    const options = courseCurriculumQueryOptions("");
    expect(options.enabled).toBe(false);
  });
});

