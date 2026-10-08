import { renderHook } from "@testing-library/react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { enrollmentQueryOptions } from "@/entities/enrollment";
import { useSpaceMyCourseProgress } from "../use-space-my-course-progress";

vi.mock("@tanstack/react-query", () => ({
  useInfiniteQuery: vi.fn(),
}));

vi.mock("@/entities/enrollment", () => ({
  enrollmentQueryOptions: {
    getMyCourseProgressInfiniteOptions: vi.fn(() => ({ queryKey: ["global"] })),
    getMyCourseProgressByAuthorInfiniteOptions: vi.fn(() => ({ queryKey: ["by-author"] })),
  },
}));

describe("useSpaceMyCourseProgress", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useInfiniteQuery).mockReturnValue({
      data: { items: [], totalCount: 0 },
      hasNextPage: false,
      fetchNextPage: vi.fn(),
      isLoading: false,
      isFetchingNextPage: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useInfiniteQuery>);
  });

  it("uses the global my-course progress feed on home", () => {
    renderHook(() => useSpaceMyCourseProgress(12));

    expect(enrollmentQueryOptions.getMyCourseProgressInfiniteOptions).toHaveBeenCalledWith({
      limit: 12,
    });
    expect(
      enrollmentQueryOptions.getMyCourseProgressByAuthorInfiniteOptions,
    ).not.toHaveBeenCalled();
  });
});
