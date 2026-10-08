import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { renderToStaticMarkup } from "react-dom/server";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CookieBanner } from "./cookie-banner";

const consent = vi.hoisted(() => ({
  hasDecided: false,
  acceptAll: vi.fn(),
  acceptNecessaryOnly: vi.fn(),
  acceptCustom: vi.fn(),
}));

vi.mock("@/shared/lib/use-cookie-consent", () => ({
  useCookieConsent: () => consent,
}));

beforeEach(() => {
  consent.hasDecided = false;
  vi.clearAllMocks();
});

async function renderHydratedBanner() {
  const container = document.createElement("div");
  container.innerHTML = renderToStaticMarkup(<CookieBanner />);
  document.body.append(container);

  const view = render(<CookieBanner />, { container, hydrate: true });
  await act(async () => Promise.resolve());

  return view;
}

describe("CookieBanner", () => {
  it("keeps the banner body out of the server-rendered HTML", () => {
    expect(renderToStaticMarkup(<CookieBanner />)).toMatchInlineSnapshot(`""`);
  });

  it("appears after hydration for a visitor who has not decided", async () => {
    await renderHydratedBanner();

    expect(await screen.findByRole("region", { name: "Настройки cookies" })).toBeInTheDocument();
  });

  it("remains absent after hydration for a visitor who has decided", async () => {
    consent.hasDecided = true;

    await renderHydratedBanner();
    await waitFor(() => {
      expect(screen.queryByRole("region", { name: "Настройки cookies" })).not.toBeInTheDocument();
    });
  });

  it("keeps consent actions and customization keyboard accessible", async () => {
    const user = userEvent.setup();
    await renderHydratedBanner();

    const controls = await screen.findByRole("group", { name: "Выбор cookies" });
    const necessaryButton = within(controls).getByRole("button", {
      name: "Принять только необходимые cookies",
    });
    const acceptAllButton = within(controls).getByRole("button", {
      name: "Принять всё",
    });

    expect(necessaryButton.className).toBe(acceptAllButton.className);

    necessaryButton.focus();
    await user.keyboard("{Enter}");
    acceptAllButton.focus();
    await user.keyboard("{Enter}");
    expect(consent.acceptNecessaryOnly).toHaveBeenCalledOnce();
    expect(consent.acceptAll).toHaveBeenCalledOnce();

    const customizeButton = within(controls).getByRole("button", { name: "Настроить cookies" });
    customizeButton.focus();
    await user.keyboard("{Enter}");

    const necessarySwitch = screen.getByRole("switch", { name: "Необходимые" });
    const analyticsSwitch = screen.getByRole("switch", { name: "Аналитические" });
    const marketingSwitch = screen.getByRole("switch", { name: "Маркетинговые" });

    expect(necessarySwitch).toBeChecked();
    expect(necessarySwitch).toBeDisabled();
    expect(analyticsSwitch).toBeChecked();
    expect(marketingSwitch).not.toBeChecked();

    analyticsSwitch.focus();
    await user.keyboard(" ");
    marketingSwitch.focus();
    await user.keyboard(" ");
    const saveButton = screen.getByRole("button", { name: "Сохранить" });
    saveButton.focus();
    await user.keyboard("{Enter}");

    expect(consent.acceptCustom).toHaveBeenCalledWith({ analytics: false, marketing: true });
  });

  it("uses a compact opaque mobile surface without backdrop filters", async () => {
    await renderHydratedBanner();

    const banner = await screen.findByRole("region", { name: "Настройки cookies" });
    const panel = banner.firstElementChild;
    const controls = screen.getByRole("group", { name: "Выбор cookies" });

    expect(banner).toHaveClass("px-2", "pb-2");
    expect(panel).toHaveClass("p-3", "bg-background");
    expect(panel?.className).not.toMatch(/backdrop-(blur|saturate)/);
    expect(screen.getByText("Cookies: базовые; аналитика — с согласия.")).toHaveClass("sm:hidden");
    expect(screen.getByText(/Обязательные cookies нужны для работы/)).toHaveClass(
      "hidden",
      "sm:inline",
    );
    expect(screen.getByText("Условия")).toHaveClass("sm:hidden");
    expect(screen.getByText("Политика cookies")).toHaveClass("hidden", "sm:inline");
    expect(screen.getByRole("link", { name: "Условия — Политика cookies" })).toHaveAttribute(
      "href",
      "/legal/cookies",
    );
    expect(controls).toHaveClass("grid", "grid-cols-3", "gap-1.5");
    expect(
      within(controls)
        .getAllByRole("button")
        .every((button) => button.classList.contains("min-h-11")),
    ).toBe(true);
  });

  it("gives every customization control a 44px mobile touch target", async () => {
    const user = userEvent.setup();
    await renderHydratedBanner();

    await user.click(await screen.findByRole("button", { name: "Настроить cookies" }));

    const backButton = screen.getByRole("button", { name: "Назад" });
    const saveButton = screen.getByRole("button", { name: "Сохранить" });
    expect(backButton).toHaveClass("min-h-11", "min-w-11");
    expect(saveButton).toHaveClass("min-h-11", "min-w-11");

    for (const touchSwitch of screen.getAllByRole("switch")) {
      expect(touchSwitch.parentElement).toHaveClass("relative", "min-h-11", "min-w-11");
      expect(touchSwitch).toHaveClass("after:absolute", "after:inset-0", "after:content-['']");
    }
  });
});
