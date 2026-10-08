import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { LessonNav } from "../lesson-nav";

const prog = { prev: { title: "Гайд", href: "/p" }, next: { title: "SoftDelete", href: "/n" } };

describe("LessonNav", () => {
  it("renders only the program cards (no label, no tasks line) when tasks are absent", () => {
    render(<LessonNav prev={prog.prev} next={prog.next} />);

    expect(screen.getByText("Гайд")).toBeInTheDocument();
    expect(screen.getByText("SoftDelete")).toBeInTheDocument();
    // No disambiguation label and no secondary stream when there's only one nav.
    expect(screen.queryByText("По программе")).not.toBeInTheDocument();
    expect(screen.queryByText("Только задачи")).not.toBeInTheDocument();
  });

  it("shows the program label + a compact tasks line when both streams exist", () => {
    render(
      <LessonNav
        prev={prog.prev}
        next={prog.next}
        tasks={{ prev: { title: "DS-18", href: "/t1" }, next: { title: "DS-20", href: "/t2" } }}
      />,
    );

    expect(screen.getByText("По программе")).toBeInTheDocument();
    expect(screen.getByText("Только задачи")).toBeInTheDocument();
    expect(screen.getByText("DS-18")).toBeInTheDocument();
    expect(screen.getByText("DS-20")).toBeInTheDocument();
  });

  it("renders nothing when there is nowhere to go", () => {
    const { container } = render(<LessonNav prev={null} next={null} />);
    expect(container).toBeEmptyDOMElement();
  });
});
