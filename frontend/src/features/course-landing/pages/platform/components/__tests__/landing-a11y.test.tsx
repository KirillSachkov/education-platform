import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FloatingTelegram } from "../floating-telegram";
import { HeroSection } from "../hero-section";
import { LiveMockup } from "../live-mockups";
import { FooterSection } from "../sections";

vi.mock("../../hooks/use-reduced-motion", () => ({ useReducedMotion: () => true }));
vi.mock("@/shared/lib/use-cookie-consent", () => ({
  useCookieConsent: () => ({ reset: vi.fn() }),
}));
vi.mock("@/shared/ui/kit/data-grid-hero", () => ({
  DataGridHero: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

describe("platform landing accessibility", () => {
  it("names the icon-only mobile Telegram link", () => {
    render(<FloatingTelegram />);

    expect(screen.getByRole("link", { name: "Консультация в Telegram" })).toBeInTheDocument();
  });

  it("does not expose decorative mock-call controls as interactive buttons", () => {
    render(<LiveMockup id="calls" />);

    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });

  it("keeps primary hero content visible during its entrance transform", () => {
    const { container } = render(<HeroSection />);
    const styles = Array.from(container.querySelectorAll("style"))
      .map((style) => style.textContent)
      .join("\n");

    expect(styles).not.toContain(".hero-in{opacity:0");
  });

  it("gives footer legal controls mobile-sized touch targets", () => {
    render(<FooterSection />);

    for (const link of screen.getAllByRole("link", {
      name: /Оферта|Политика ПДн|Согласие ПДн|Cookies|Согласие на рассылку/,
    })) {
      expect(link).toHaveClass("min-h-11");
    }
    expect(screen.getByRole("button", { name: "Настройки cookies" })).toHaveClass("min-h-11");
  });
});
