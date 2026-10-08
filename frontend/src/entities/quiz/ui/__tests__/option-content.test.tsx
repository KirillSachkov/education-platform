import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { QuizOptionContent, QuizRichText } from "../option-content";

describe("QuizOptionContent", () => {
  it("renders plain text as a span", () => {
    const { container } = render(<QuizOptionContent text="Simple answer text" />);
    const span = container.querySelector("span");
    expect(span).not.toBeNull();
    expect(span?.textContent).toBe("Simple answer text");
  });

  it("renders text with backtick code via MarkdownContent", () => {
    const { container } = render(
      <QuizOptionContent text="Use `LogDebug()` to log" />,
    );
    const wrapper = container.querySelector("div");
    expect(wrapper).not.toBeNull();
  });

  /**
   * Regression: #543 — inline code must NOT get white-space:pre forced on
   * mobile. The wrapper must scope the !whitespace-pre override to `pre code`
   * (block code) only, not to bare `code` (inline code). Bare `code` can then
   * wrap via the global `overflow-wrap:anywhere` rule.
   */
  it("wrapper className scopes whitespace-pre to pre code only (not bare code)", () => {
    const longInlineCode = "`LogDebug($\"Processed orderId in ms\")`";
    const { container } = render(<QuizOptionContent text={longInlineCode} />);
    const wrapper = container.querySelector("div");
    expect(wrapper).not.toBeNull();
    // Must NOT contain the broad [&_code]:!whitespace-pre that overrides inline code.
    expect(wrapper?.className).not.toContain("[&_code]:!whitespace-pre");
    // Must contain the scoped [&_pre_code]:!whitespace-pre for code blocks.
    expect(wrapper?.className).toContain("[&_pre_code]:!whitespace-pre");
  });
});

describe("QuizRichText", () => {
  it("renders plain text (no code) as a span, not a code block", () => {
    const { container } = render(<QuizRichText text="Что выведет код? Ничего особенного." />);
    const span = container.querySelector("span");
    expect(span).not.toBeNull();
    expect(span?.textContent).toBe("Что выведет код? Ничего особенного.");
    expect(container.querySelector("pre")).toBeNull();
  });

  it("renders code via MarkdownContent wrapper when text has a fenced block", () => {
    const { container } = render(
      <QuizRichText text={"Что выведет код?\n\n```csharp\nConsole.Write(1);\n```"} />,
    );
    const wrapper = container.querySelector("div");
    expect(wrapper).not.toBeNull();
    expect(wrapper?.className).toContain("[&_pre_code]:!whitespace-pre");
  });

  it("forwards className to the plain span", () => {
    const { container } = render(<QuizRichText text="plain" className="text-xs" />);
    expect(container.querySelector("span")?.className).toContain("text-xs");
  });

  it("scopes whitespace-pre to pre code only (mobile inline-code regression #543)", () => {
    const { container } = render(<QuizRichText text="inline `code` here" />);
    const wrapper = container.querySelector("div");
    expect(wrapper?.className).not.toContain("[&_code]:!whitespace-pre");
    expect(wrapper?.className).toContain("[&_pre_code]:!whitespace-pre");
  });
});
