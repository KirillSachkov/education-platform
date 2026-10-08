import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { CourseCurriculumDto } from "@/entities/course";

const mocks = vi.hoisted(() => ({
  fetchAnonymous: vi.fn<(path: string, init?: RequestInit) => Promise<unknown>>(),
  courseJsonLd: vi.fn<(props: { name: string; description: string }) => void>(),
}));

vi.mock("@/shared/seo", () => ({
  buildEntityMetadata: vi.fn((input: unknown) => input),
  CourseJsonLd: (props: { name: string; description: string }) => {
    mocks.courseJsonLd(props);
    return <script data-name={props.name} data-description={props.description} />;
  },
  fetchAnonymous: (path: string, init?: RequestInit) => mocks.fetchAnonymous(path, init),
}));

vi.mock("../content", () => ({
  CoursePageContent: ({ initialCourse }: { initialCourse?: CourseCurriculumDto | null }) => (
    <main>
      <h1>{initialCourse?.title}</h1>
      <p>{initialCourse?.description}</p>
      {initialCourse?.sections.flatMap((section) =>
        section.items.map((item) => <a key={item.id}>{item.title}</a>),
      )}
    </main>
  ),
}));

import CoursePage, { generateMetadata } from "../page";

const curriculum = {
  id: "course-id",
  authorId: "author-id",
  slug: "seo-course",
  title: "SEO-курс",
  description: "Уникальное описание курса",
  imageUrl: "/course-cover.webp",
  authorDisplayName: "Кирилл",
  sections: [{ id: "module", items: [{ id: "lesson", title: "Урок SSR" }] }],
} as CourseCurriculumDto;

describe("course detail server data", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchAnonymous.mockImplementation((path) => {
      if (path === "/courses/by-slug/seo-course/") {
        return Promise.resolve({ courseId: "course-id", slug: "seo-course" });
      }
      if (path === "/courses/course-id/curriculum/") return Promise.resolve(curriculum);
      return Promise.resolve(null);
    });
  });

  it("passes the anonymous curriculum into the raw course HTML", async () => {
    const html = renderToStaticMarkup(
      await CoursePage({ params: Promise.resolve({ courseSlug: "seo-course" }) }),
    );

    expect(html).toContain("<h1>SEO-курс</h1>");
    expect(html).toContain("Уникальное описание курса");
    expect(html).toContain("Урок SSR");
    expect(mocks.fetchAnonymous).toHaveBeenCalledWith("/courses/course-id/curriculum/", undefined);
    expect(mocks.courseJsonLd).toHaveBeenCalledWith(
      expect.objectContaining({ name: curriculum.title, description: curriculum.description }),
    );
  });

  it("uses the same curriculum for canonical metadata", async () => {
    const metadata = await generateMetadata({
      params: Promise.resolve({ courseSlug: "seo-course" }),
    });

    expect(metadata).toEqual(
      expect.objectContaining({
        title: curriculum.title,
        description: curriculum.description,
        path: "/courses/seo-course",
      }),
    );
  });
});
