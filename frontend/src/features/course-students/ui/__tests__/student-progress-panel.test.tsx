import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { CourseBuilderDto } from "@/entities/course";

vi.mock("@/entities/course-student", () => ({
  studentProgressQueryOptions: () => ({
    queryKey: ["course-students", "progress"],
    queryFn: vi.fn(),
  }),
}));

vi.mock("@/entities/course", () => ({}));

vi.mock("@/entities/module", () => ({}));

vi.mock("@/shared/api", () => ({
  getErrorMessage: () => "Ошибка",
}));

vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({
    data: {
      enrollmentStarted: true,
      completedMaterials: [],
      issues: [],
    },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));

vi.mock("../../model/use-mark-material-viewed-for-user", () => ({
  useMarkMaterialViewedForUser: () => ({
    markMaterialViewedForUser: vi.fn(),
    pendingMaterialId: null,
  }),
}));

vi.mock("../../model/use-set-issue-status-for-user", () => ({
  useSetIssueStatusForUser: () => ({
    setIssueStatusForUser: vi.fn(),
    pendingIssueId: null,
  }),
}));

vi.mock("@/shared/ui/components", () => ({
  UserAvatar: ({ name, className }: { name: string; className?: string }) => (
    <div data-testid="avatar" className={className}>
      {name}
    </div>
  ),
}));

import { StudentProgressPanel } from "../student-progress-panel";

const course = {
  sections: [],
} as unknown as CourseBuilderDto;

describe("StudentProgressPanel", () => {
  it("keeps progress content in a viewport-bounded scroll container", () => {
    render(
      <StudentProgressPanel
        courseId="course-1"
        course={course}
        userId="user-1"
        studentName="Student"
        avatarId={null}
        open
        onOpenChange={vi.fn()}
      />,
    );

    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveClass("max-h-dvh");
    expect(dialog).toHaveClass("min-h-0");
    expect(dialog).toHaveClass("overflow-hidden");

    const scrollArea = dialog.querySelector('[data-slot="scroll-area"]');
    expect(scrollArea).not.toBeNull();
    expect(scrollArea).toHaveClass("flex-1");
    expect(scrollArea).toHaveClass("min-h-0");
  });
});
