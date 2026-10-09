import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CreateCourseDialog } from "./create-course-dialog";

const mocks = vi.hoisted(() => ({ createCourse: vi.fn() }));
vi.mock("../model/use-create-course", () => ({
  useCreateCourse: () => ({ createCourse: mocks.createCourse }),
}));

describe("course creation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.createCourse.mockResolvedValue({ result: "course-id" });
  });

  it("creates a course after retiring tag assignment", async () => {
    const onOpenChange = vi.fn();
    render(<CreateCourseDialog open onOpenChange={onOpenChange} />);
    fireEvent.change(screen.getByLabelText("Название"), { target: { value: "PostgreSQL" } });
    fireEvent.change(screen.getByLabelText("Описание"), { target: { value: "Практика запросов" } });
    fireEvent.change(screen.getByLabelText("URL-slug"), { target: { value: "postgresql" } });
    fireEvent.click(screen.getByRole("button", { name: /^Создать$/ }));

    await waitFor(() => expect(mocks.createCourse).toHaveBeenCalledWith({
      title: "PostgreSQL", description: "Практика запросов", slug: "postgresql", kind: "COURSE",
    }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
