import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToString } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";

vi.mock("next/navigation", () => ({ notFound: vi.fn() }));

import { CourseSlugResolver } from "../course-slug-resolver";

describe("CourseSlugResolver SSR initial data", () => {
  it("renders children before JavaScript and immediately revalidates the anonymous resolution", () => {
    const queryClient = new QueryClient();
    const initialCourse = { courseId: "course-id", slug: "seo-course" };

    const html = renderToString(
      <QueryClientProvider client={queryClient}>
        <CourseSlugResolver courseSlug="seo-course" initialCourse={initialCourse}>
          <h1>Server-rendered course</h1>
        </CourseSlugResolver>
      </QueryClientProvider>,
    );

    expect(html).toContain("Server-rendered course");
    expect(html).not.toContain("animate-spin");
    expect(queryClient.getQueryState(["courses", "by-slug", "seo-course"])?.dataUpdatedAt).toBe(0);
  });
});
