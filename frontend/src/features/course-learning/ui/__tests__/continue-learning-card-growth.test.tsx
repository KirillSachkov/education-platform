import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { CurriculumItemDto, CurriculumSectionDto } from "@/entities/course";

const trackGrowthEvent = vi.hoisted(() => vi.fn());

vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));

import { ContinueLearningCard } from "../continue-learning-card";

const section: CurriculumSectionDto = {
  id: "section-1",
  itemType: "Section",
  title: "Основы C#",
  description: null,
  detailedDescription: null,
  sortKey: "a",
  isOptional: false,
  items: [],
};

function makeItem(overrides: Partial<CurriculumItemDto> = {}): CurriculumItemDto {
  return {
    id: "material-1",
    itemType: "Material",
    title: "Первый материал",
    sortKey: "a",
    isOptional: false,
    position: 1,
    accessType: "PUBLIC",
    viewPriority: null,
    materialKind: "ARTICLE",
    durationSeconds: null,
    coverUrl: null,
    ...overrides,
  };
}

describe("ContinueLearningCard growth analytics", () => {
  beforeEach(() => trackGrowthEvent.mockClear());

  it.each([
    ["Material", "material"],
    ["Issue", "issue"],
  ] as const)("tracks a %s continuation without identity data", (itemType, targetType) => {
    render(
      <ContinueLearningCard
        item={makeItem({ itemType, id: `${targetType}-1` })}
        section={section}
        sectionNumber={1}
        courseSlug="dotnet"
      />,
    );

    fireEvent.click(screen.getByRole("link"));

    expect(trackGrowthEvent).toHaveBeenCalledWith({
      name: "continue_learning_click",
      properties: {
        target_type: targetType,
        content_id: `${targetType}-1`,
      },
    });
  });
});
