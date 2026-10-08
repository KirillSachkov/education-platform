import { beforeEach, describe, expect, it, vi } from "vitest";
import { EntityTypes } from "@/shared/config/entity-types";

const get = vi.hoisted(() => vi.fn());

vi.mock("@/shared/api", () => ({
  apiClient: { get },
}));

import { searchApi, searchDocumentsQueryOptions } from "../api";

describe("search public-only access filter", () => {
  beforeEach(() => {
    get.mockReset();
    get.mockResolvedValue({
      data: {
        result: { hits: [], facets: [], totalCount: 0, page: 1, pageSize: 20 },
      },
    });
  });

  it("sends the exact public filter to SearchService", async () => {
    await searchApi.getDocuments({
      entityTypes: [EntityTypes.MATERIAL],
      accessFilter: "public",
    });

    expect(get.mock.calls[0]?.[0]).toBe("/search/");
    expect(get.mock.calls[0]?.[1]).toMatchObject({
      params: {
        accessFilter: "public",
        entityTypes: ["Material"],
      },
    });
  });

  it("keeps public-only and legacy free results in separate cache entries", () => {
    expect(searchDocumentsQueryOptions({ accessFilter: "public" }).queryKey).not.toEqual(
      searchDocumentsQueryOptions({ accessFilter: "free" }).queryKey,
    );
  });
});
