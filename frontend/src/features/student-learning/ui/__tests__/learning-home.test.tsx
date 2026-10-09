import { render, screen, fireEvent } from "@testing-library/react";
import type * as ReactQuery from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
const state = vi.hoisted(() => ({
  courses: [] as unknown[],
  loading: false,
  error: null as Error | null,
  next: false,
  fetch: vi.fn(),
  retry: vi.fn(),
}));
vi.mock("../../model/use-space-my-course-progress", () => ({
  useSpaceMyCourseProgress: () => ({
    items: state.courses,
    totalCount: state.courses.length,
    isLoading: state.loading,
    error: state.error,
    hasNextPage: state.next,
    isFetchingNextPage: false,
    fetchNextPage: state.fetch,
    refetch: state.retry,
  }),
}));
vi.mock("@tanstack/react-query", async (importOriginal) => ({
  ...(await importOriginal<typeof ReactQuery>()),
  useQuery: () => ({ data: null, isLoading: false, error: null }),
}));
import { AuthenticatedHome } from "../space-home-dashboard";
const course = (title: string, slug: string, kind: string) => ({
  title,
  courseSlug: slug,
  kind,
  courseId: slug,
  enrollmentId: slug,
  totalItems: 10,
  completedItems: 0,
  totalModules: 0,
  totalMaterials: 10,
  totalIssues: 0,
  progressPercent: 0,
  imageUrl: null,
  isNew: false,
});
beforeEach(() => {
  state.courses = [];
  state.loading = false;
  state.error = null;
  state.next = false;
  vi.clearAllMocks();
});
describe("My learning", () => {
  it("keeps older purchased courses, intensives and marathons without a public catalog", () => {
    state.courses = [
      course("DevOps", "devops", "COURSE"),
      course("Интенсив", "old-intensive", "INTENSIVE"),
      course("Марафон", "old-marathon", "MARATHON"),
    ];
    render(<AuthenticatedHome />);
    for (const slug of ["devops", "old-intensive", "old-marathon"])
      expect(
        screen
          .getAllByRole("link")
          .some((link) => link.getAttribute("href") === `/courses/${slug}`),
      ).toBe(true);
    expect(screen.queryByText("Каталог курсов")).not.toBeInTheDocument();
  });
  it("offers pricing when the learner has no covered courses", () => {
    render(<AuthenticatedHome />);
    expect(screen.getByRole("link", { name: "Посмотреть доступ" })).toHaveAttribute(
      "href",
      "/pricing",
    );
  });
  it("lets the learner load remaining covered courses", () => {
    state.courses = [course("DevOps", "devops", "COURSE")];
    state.next = true;
    render(<AuthenticatedHome />);
    fireEvent.click(screen.getByRole("button", { name: /Показать ещё/ }));
    expect(state.fetch).toHaveBeenCalledOnce();
  });
  it("offers retry when course access cannot be loaded", () => {
    state.error = new Error("Unavailable");
    render(<AuthenticatedHome />);
    fireEvent.click(screen.getByRole("button"));
    expect(state.retry).toHaveBeenCalledOnce();
  });
});
