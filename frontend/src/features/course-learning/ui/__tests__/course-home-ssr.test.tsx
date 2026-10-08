import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderToString } from "react-dom/server";
import { describe, expect, it, vi } from "vitest";
import type { CourseCurriculumDto } from "@/entities/course";
import { CourseIdProvider } from "@/shared/providers/course-id-provider";

vi.mock("next/navigation", () => ({
  usePathname: () => "/courses/seo-course",
  useRouter: () => ({ replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(),
}));

vi.mock("@/entities/notification", () => ({
  CourseSubscribeButton: () => null,
}));

vi.mock("@/entities/tag", () => ({
  SearchableTagsField: () => null,
}));

vi.mock("@/features/course-learning/model/use-resolved-course-access", () => ({
  useResolvedCourseAccess: () => ({
    accessLevel: "anonymous",
    canAccessItem: (accessType: string | null) => accessType === "PUBLIC",
    hasActiveEnrollment: false,
    isAuthenticated: false,
  }),
}));

vi.mock("@/features/course-learning/ui/whats-new-section", () => ({
  WhatsNewSection: () => null,
}));

import { CourseHome } from "../course-home";

const initialCourse: CourseCurriculumDto = {
  id: "course-id",
  authorId: "author-id",
  slug: "seo-course",
  title: "SEO-курс по .NET",
  description: "Описание, которое должно быть в исходном HTML.",
  status: "PUBLISHED",
  kind: "COURSE",
  imageId: null,
  imageUrl: null,
  gettingStartedModuleId: null,
  hasFreeContent: true,
  isNew: false,
  createdAt: "2026-07-01T00:00:00Z",
  updatedAt: "2026-07-14T00:00:00Z",
  learningOutcomes: ["Проектировать API"],
  targetAudience: [],
  prerequisites: [],
  collections: [],
  sections: [
    {
      id: "module-id",
      itemType: "Module",
      title: "Модуль архитектуры",
      description: null,
      detailedDescription: null,
      sortKey: "a0",
      isOptional: false,
      items: [
        {
          id: "material-id",
          itemType: "Material",
          title: "Публичный урок",
          sortKey: "a0",
          isOptional: false,
          position: 1,
          accessType: "PUBLIC",
          viewPriority: null,
          materialKind: "ARTICLE",
          durationSeconds: null,
          coverUrl: null,
        },
      ],
    },
  ],
};

describe("CourseHome SSR initial data", () => {
  it("renders the anonymous curriculum immediately and marks it stale for auth refetch", () => {
    const queryClient = new QueryClient();

    const html = renderToString(
      <QueryClientProvider client={queryClient}>
        <CourseIdProvider courseId={initialCourse.id} courseSlug={initialCourse.slug}>
          <CourseHome courseId={initialCourse.id} initialCourse={initialCourse} />
        </CourseIdProvider>
      </QueryClientProvider>,
    );

    expect(html).toContain("SEO-курс по .NET");
    expect(html).toContain("Описание, которое должно быть в исходном HTML.");
    expect(html).toContain("Модуль архитектуры");
    expect(html).toContain("Публичный урок");
    expect(html).toContain("/courses/seo-course/learn/material-id");
    expect(
      queryClient.getQueryState(["courses", initialCourse.id, "curriculum"])?.dataUpdatedAt,
    ).toBe(0);
  });
});
