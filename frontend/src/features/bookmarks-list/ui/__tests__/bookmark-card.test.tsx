import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import type { BookmarkedMaterialDto } from "@/entities/bookmark";
const actions = vi.hoisted(() => ({ push: vi.fn(), toggle: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push: actions.push }) }));
vi.mock("@/entities/bookmark", () => ({
  useBookmarkToggle: () => ({ toggleBookmark: actions.toggle, isPending: false }),
}));
import { BookmarkCard } from "../bookmark-card";
const item: BookmarkedMaterialDto = {
  courseId: "old-course",
  courseSlug: "devops",
  courseTitle: "DevOps",
  target: { type: "Lesson", id: "lesson" },
  title: "Saved lesson",
  sectionTitle: "Basics",
  sectionType: "Module",
  createdAt: "2026-10-01T10:00:00Z",
  isAccessible: true,
  lockReason: null,
};
beforeEach(() => vi.clearAllMocks());
it("opens the purchased lesson with the keyboard", () => {
  render(<BookmarkCard item={item} />);
  fireEvent.keyDown(screen.getByRole("link"), { key: "Enter" });
  expect(actions.push).toHaveBeenCalledWith("/courses/devops/learn/lesson");
});
it("removes a bookmark without navigating to the lesson", () => {
  render(<BookmarkCard item={item} />);
  const remove = screen.getByRole("button", { name: "Удалить закладку" });
  fireEvent.keyDown(remove, { key: "Enter" });
  expect(actions.push).not.toHaveBeenCalled();
  fireEvent.click(remove);
  fireEvent.click(screen.getByRole("button", { name: /^Удалить$/ }));
  expect(actions.toggle).toHaveBeenCalledWith(true);
  expect(actions.push).not.toHaveBeenCalled();
});
