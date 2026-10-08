import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { LockedContentPlaceholder } from "../locked-content-placeholder";

describe("LockedContentPlaceholder", () => {
  it("декоративный: aria-hidden + не выделяется, без утечки текста", () => {
    const { container } = render(<LockedContentPlaceholder lines={3} />);
    const root = container.firstElementChild as HTMLElement;
    expect(root).toHaveAttribute("aria-hidden", "true");
    expect(root.className).toContain("select-none");
    // Сервер уже вырезал контент — рисуем только полосы, без текста.
    expect(root.textContent).toBe("");
  });

  it("рендерит ровно `lines` скелетон-полос под стем", () => {
    const { container } = render(<LockedContentPlaceholder lines={3} />);
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBe(3);
  });

  it("добавляет строки-варианты при options (lines + options)", () => {
    const { container } = render(<LockedContentPlaceholder lines={2} options={4} />);
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBe(6);
  });

  it("минимум одна полоса даже при lines=0", () => {
    const { container } = render(<LockedContentPlaceholder lines={0} />);
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBe(1);
  });
});
