import { act, render, waitFor } from "@testing-library/react";
import { useEffect } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { YANDEX_METRIKA_READY_EVENT } from "@/shared/analytics";

const consentState = vi.hoisted(() => ({ analytics: true }));

vi.mock("@/shared/lib/use-cookie-consent", () => ({
  useCookieConsent: () => ({ consent: { analytics: consentState.analytics } }),
}));

vi.mock("next/script", () => ({
  default: function MockScript({ id, children }: { id: string; children: string }) {
    useEffect(() => {
      window.__sachkovMetrikaInitialized = true;
      window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
    }, []);
    return <script id={id}>{children}</script>;
  },
}));

describe("YandexMetrikaScript", () => {
  beforeEach(() => {
    vi.resetModules();
    vi.stubEnv("NEXT_PUBLIC_YANDEX_METRIKA_ID", "12345678");
    consentState.analytics = true;
    delete window.__sachkovMetrikaInitialized;
    window.ym = vi.fn();
  });

  it("destructs the SPA counter on withdrawal and reinitializes it on renewed consent", async () => {
    const { YandexMetrikaScript } = await import("./yandex-metrika-script");
    const view = render(<YandexMetrikaScript />);

    await act(async () => Promise.resolve());

    consentState.analytics = false;
    view.rerender(<YandexMetrikaScript />);
    await waitFor(() => {
      expect(window.ym).toHaveBeenCalledWith(12345678, "destruct");
    });

    consentState.analytics = true;
    view.rerender(<YandexMetrikaScript />);
    await waitFor(() => {
      expect(window.ym).toHaveBeenCalledWith(12345678, "init", {
        clickmap: true,
        trackLinks: true,
        accurateTrackBounce: true,
        webvisor: true,
      });
    });
  });
});
