import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { ProgramPreviewSection } from "../../lib/program-preview";
import { CourseProgramPreview } from "../course-program-preview";

// CurriculumSectionCard (shared with the Программа page) pulls a heavy import chain
// (lock-copy, markdown, next-auth) that the jsdom test env can't resolve. We only
// need to assert that the preview composes it — render a light stub and inspect the
// props it receives (title, number, kind, defaultOpen). vi.mock is hoisted above the
// import above, so CourseProgramPreview picks up the stub.
vi.mock("../curriculum-section-card", () => ({
  CurriculumSectionCard: (props: {
    section: { id: string; title: string };
    sectionNumber: number;
    sectionKind: string;
    defaultOpen: boolean;
  }) => (
    <div
      data-testid="section-card"
      data-kind={props.sectionKind}
      data-open={props.defaultOpen}
      data-number={props.sectionNumber}
    >
      <h3>{props.section.title}</h3>
    </div>
  ),
}));

function previewSection(
  id: string,
  sectionNumber: number,
  isActive = false,
): ProgramPreviewSection {
  return {
    section: {
      id,
      itemType: "Module",
      title: `Модуль ${sectionNumber}`,
      description: null,
      detailedDescription: null,
      sortKey: id,
      isOptional: false,
      items: [],
    },
    sectionNumber,
    isActive,
  };
}

function previewSections(count: number, activeIndex = -1): ProgramPreviewSection[] {
  return Array.from({ length: count }, (_, index) =>
    previewSection(`m${index + 1}`, index + 1, index === activeIndex),
  );
}

describe("CourseProgramPreview", () => {
  it("composes the curriculum module card for each preview section", () => {
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={[previewSection("m1", 1), previewSection("m2", 2, true)]}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    expect(screen.getByRole("heading", { name: "Программа курса" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Вся программа/ })).toHaveAttribute(
      "href",
      "/courses/devops/program",
    );

    // One CurriculumSectionCard (program-page design) per preview section.
    const cards = screen.getAllByTestId("section-card");
    expect(cards).toHaveLength(2);
    expect(screen.getByRole("heading", { name: "Модуль 1" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Модуль 2" })).toBeInTheDocument();

    // The active module is expanded by default, mirroring the Программа page.
    expect(cards[0]).toHaveAttribute("data-open", "false");
    expect(cards[1]).toHaveAttribute("data-open", "true");
  });

  it("marks the getting-started module with the getting-started kind", () => {
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={[previewSection("m1", 1)]}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId="m1"
      />,
    );

    expect(screen.getByTestId("section-card")).toHaveAttribute("data-kind", "getting-started");
  });

  it("renders all modules without a «Показать ещё» button when there are 10 or fewer (#662)", () => {
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={previewSections(10)}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    expect(screen.getAllByTestId("section-card")).toHaveLength(10);
    expect(screen.queryByRole("button", { name: /Показать ещё/ })).not.toBeInTheDocument();
  });

  it("shows only the first 10 modules with a «Показать ещё» button when there are more (#662)", () => {
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={previewSections(12)}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    // First page: modules 1..10 in order, last two hidden behind the button.
    const cards = screen.getAllByTestId("section-card");
    expect(cards).toHaveLength(10);
    expect(cards[0]).toHaveAttribute("data-number", "1");
    expect(cards[9]).toHaveAttribute("data-number", "10");
    expect(screen.queryByRole("heading", { name: "Модуль 11" })).not.toBeInTheDocument();

    // Button announces how many modules are still hidden (with Russian plural).
    expect(screen.getByRole("button", { name: /Показать ещё 2 модуля/ })).toBeInTheDocument();
  });

  it("reveals the next page of modules when «Показать ещё» is clicked (#662)", () => {
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={previewSections(12)}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: /Показать ещё/ }));

    // All 12 modules visible now; button disappears once nothing is left to reveal.
    expect(screen.getAllByTestId("section-card")).toHaveLength(12);
    expect(screen.getByRole("heading", { name: "Модуль 12" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Показать ещё/ })).not.toBeInTheDocument();
  });

  it("keeps an off-page active module collapsed until «Показать ещё» reveals it (#662)", () => {
    // 15 modules, the active one at index 11 (module 12 — beyond the first page of 10).
    render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={previewSections(15, 11)}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    // First page: 10 modules, NONE expanded — the active module is hidden behind the button.
    const firstPage = screen.getAllByTestId("section-card");
    expect(firstPage).toHaveLength(10);
    expect(firstPage.every((card) => card.getAttribute("data-open") === "false")).toBe(true);
    expect(screen.getByRole("button", { name: /Показать ещё 5 модулей/ })).toBeInTheDocument();

    // Reveal the rest → module 12 becomes visible and is expanded by default.
    fireEvent.click(screen.getByRole("button", { name: /Показать ещё/ }));
    const allCards = screen.getAllByTestId("section-card");
    expect(allCards).toHaveLength(15);
    const activeCard = allCards.find((card) => card.getAttribute("data-number") === "12");
    expect(activeCard).toHaveAttribute("data-open", "true");
  });

  it("renders nothing when there are no modules to preview", () => {
    const { container } = render(
      <CourseProgramPreview
        courseSlug="devops"
        sections={[]}
        learningState={null}
        accessLevel="standard"
        gettingStartedModuleId={null}
      />,
    );

    expect(container).toBeEmptyDOMElement();
  });
});
