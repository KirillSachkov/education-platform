import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ComponentProps } from "react";

import { IssueSubmissionForm } from "../issue-submission-form";

// PR-URL валидация формы сдачи (#718). Форма — чистый props-компонент: field-error
// вычисляется из submissionText, а disabled кнопки — из внешнего canSubmit.
function renderForm(overrides: Partial<ComponentProps<typeof IssueSubmissionForm>> = {}) {
  return render(
    <IssueSubmissionForm
      status="IN_PROGRESS"
      submissionMode="PULL_REQUEST"
      submissionText=""
      onSubmissionTextChange={vi.fn()}
      onSubmit={vi.fn()}
      onStart={vi.fn()}
      canStart
      canSubmit={false}
      {...overrides}
    />,
  );
}

describe("IssueSubmissionForm PR-URL validation (#718)", () => {
  it("shows an inline field-error for a non-PR github link and disables submit", () => {
    renderForm({
      submissionText: "https://github.com/wolonee/DirectoryService/pull/new/DS-F15",
      canSubmit: false,
    });

    expect(screen.getByTestId("pr-url-error")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Отправить/ })).toBeDisabled();
  });

  it("shows no field-error and enables submit for a valid PR URL", () => {
    renderForm({
      submissionText: "https://github.com/owner/repo/pull/123",
      canSubmit: true,
    });

    expect(screen.queryByTestId("pr-url-error")).toBeNull();
    expect(screen.getByRole("button", { name: /Отправить/ })).toBeEnabled();
  });

  it("does not nag with a field-error while the input is still empty", () => {
    renderForm({ submissionText: "", canSubmit: false });

    expect(screen.queryByTestId("pr-url-error")).toBeNull();
  });

  it("marks the input aria-invalid only for an invalid PR URL", () => {
    const { rerender } = renderForm({ submissionText: "not-a-url", canSubmit: false });
    expect(screen.getByRole("textbox")).toHaveAttribute("aria-invalid", "true");

    rerender(
      <IssueSubmissionForm
        status="IN_PROGRESS"
        submissionMode="PULL_REQUEST"
        submissionText="https://github.com/owner/repo/pull/7"
        onSubmissionTextChange={vi.fn()}
        onSubmit={vi.fn()}
        onStart={vi.fn()}
        canStart
        canSubmit
      />,
    );
    expect(screen.getByRole("textbox")).not.toHaveAttribute("aria-invalid", "true");
  });

  it("never shows the PR-URL error in self-check mode (free-form textarea)", () => {
    renderForm({
      submissionMode: "SELF_CHECK",
      submissionText: "Я проверил, что решение запускается локально",
      canSubmit: true,
    });

    expect(screen.queryByTestId("pr-url-error")).toBeNull();
    expect(screen.getByRole("button", { name: /Проверить себя/ })).toBeEnabled();
  });
});
