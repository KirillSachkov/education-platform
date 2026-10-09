import { beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient, InfiniteQueryObserver } from "@tanstack/react-query";
import { courseMaterialsFeedQueryOptions, materialsApi } from "../api";

const mocks = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/shared/api", () => ({ apiClient: { get: mocks.get } }));

const envelope = (id: string, nextCursor: string | null = null) => ({
  data: { result: { items: [{ id }], nextCursor, totalCount: 0 } },
});

describe("course material search", () => {
  beforeEach(() => vi.clearAllMocks());

  it("sends search, kind and cursor only to the selected course feed", async () => {
    const signal = new AbortController().signal;
    mocks.get.mockResolvedValue(envelope("material"));
    await materialsApi.getCourseMaterialsFeed("course-one", {
      search: "PostgreSQL",
      kind: "VIDEO",
      cursor: "next-page",
      limit: 20,
      signal,
    });
    expect(mocks.get).toHaveBeenCalledWith(
      "/courses/course-one/materials/feed/",
      expect.objectContaining({
        params: { search: "PostgreSQL", kind: "VIDEO", cursor: "next-page", limit: 20 },
        signal,
      }),
    );
  });

  it("keeps separate course/search pages and follows the returned cursor", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    mocks.get.mockResolvedValueOnce(envelope("first", "cursor-two"));
    const firstOptions = courseMaterialsFeedQueryOptions("course-one", { search: "SQL" });
    const first = await client.fetchInfiniteQuery(firstOptions);
    expect(first.pages[0].result?.items[0].id).toBe("first");
    expect(firstOptions.getNextPageParam(first.pages[0], first.pages, undefined, [])).toBe(
      "cursor-two",
    );

    mocks.get.mockResolvedValueOnce(envelope("other-course"));
    const second = await client.fetchInfiniteQuery(
      courseMaterialsFeedQueryOptions("course-two", { search: "SQL" }),
    );
    expect(second.pages[0].result?.items[0].id).toBe("other-course");
    expect(client.getQueryData(firstOptions.queryKey)).toEqual(first);
    client.clear();
  });

  it("does not show the previous course while the next course request loads", async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    mocks.get.mockResolvedValueOnce(envelope("first"));
    const firstOptions = courseMaterialsFeedQueryOptions("course-one");
    await client.fetchInfiniteQuery(firstOptions);
    const observer = new InfiniteQueryObserver(client, firstOptions);
    expect(observer.getCurrentResult().data?.items[0].id).toBe("first");
    observer.setOptions(courseMaterialsFeedQueryOptions("course-two"));
    expect(observer.getCurrentResult().data).toBeUndefined();
    observer.destroy();
    client.clear();
  });
});
