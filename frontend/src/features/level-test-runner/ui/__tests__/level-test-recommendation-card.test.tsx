import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LevelTestRecommendationCard } from "../level-test-recommendation-card";

const { trackGrowthEvent } = vi.hoisted(() => ({ trackGrowthEvent: vi.fn() }));

vi.mock("@/entities/course", () => ({
  courseDetailQueryOptions: () => ({ queryKey: ["course", "course-789"] }),
}));

vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({
    data: {
      id: "course-789",
      slug: "dotnet-developer",
      title: ".NET Developer",
      description: "Course description",
      authorDisplayName: null,
      authorAvatarUrl: null,
    },
    isPending: false,
  }),
}));

vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));

describe("LevelTestRecommendationCard", () => {
  beforeEach(() => vi.clearAllMocks());

  it("tracks a recommended course click with allowed identifiers", async () => {
    const user = userEvent.setup();
    render(
      <LevelTestRecommendationCard
        testId="test-123"
        recommendedCourseId="course-789"
        weakestTitles={[]}
      />,
    );

    const courseLink = screen.getByRole("link", { name: /Перейти к курсу/ });
    courseLink.addEventListener("click", (event) => {
      event.preventDefault();
    });
    await user.click(courseLink);

    expect(trackGrowthEvent).toHaveBeenCalledWith({
      name: "level_test_recommendation_clicked",
      properties: { test_id: "test-123", course_id: "course-789" },
    });
  });
});
