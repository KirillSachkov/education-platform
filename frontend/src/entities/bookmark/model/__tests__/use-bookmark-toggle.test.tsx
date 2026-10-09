import { act, renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { bookmarksApi } from "../../api";
import { useBookmarkToggle } from "../use-bookmark-toggle";
vi.mock("sonner", () => ({ toast: { error: vi.fn() } }));

describe("course bookmarks", () => {
  it("saves and removes a purchased lesson, refreshing saved lists", async () => {
    const put = vi.spyOn(bookmarksApi, "putBookmark").mockResolvedValue("bookmark");
    const remove = vi.spyOn(bookmarksApi, "deleteBookmark").mockResolvedValue("bookmark");
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(client, "invalidateQueries");
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(
      () =>
        useBookmarkToggle({ courseId: "old-course", entityType: "Lesson", entityId: "old-lesson" }),
      { wrapper },
    );
    await act(() => result.current.toggleBookmark(false));
    expect(put).toHaveBeenCalledWith("old-course", "Lesson", "old-lesson");
    await act(() => result.current.toggleBookmark(true));
    expect(remove).toHaveBeenCalledWith("old-course", "Lesson", "old-lesson");
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ["bookmarks", "list"] });
    client.clear();
  });
  it("restores the bookmark cache after access is denied", async () => {
    vi.spyOn(bookmarksApi, "deleteBookmark").mockRejectedValueOnce(new Error("Access denied"));
    const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
    const key = ["bookmarks", "my-ids", "old-course"];
    const data = {
      pages: [
        {
          items: [
            {
              courseId: "old-course",
              target: { type: "Lesson", id: "old-lesson" },
              createdAt: "2026-10-01",
            },
          ],
          totalCount: 1,
        },
      ],
      pageParams: [undefined],
    };
    client.setQueryData(key, data);
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={client}>{children}</QueryClientProvider>
    );
    const { result } = renderHook(
      () =>
        useBookmarkToggle({ courseId: "old-course", entityType: "Lesson", entityId: "old-lesson" }),
      { wrapper },
    );
    await act(async () => {
      await expect(result.current.toggleBookmark(true)).rejects.toThrow("Access denied");
    });
    await waitFor(() => expect(client.getQueryData(key)).toEqual(data));
    client.clear();
  });
});
