import { renderToStaticMarkup } from "react-dom/server";
import { createElement } from "react";
import { describe, expect, it } from "vitest";

import { SidebarProvider, useSidebar } from "@/shared/ui/kit/sidebar";

function StateProbe() {
  const { state } = useSidebar();
  return createElement("span", { id: "state" }, state);
}

function ssr(defaultOpen: boolean) {
  return renderToStaticMarkup(
    createElement(SidebarProvider, { defaultOpen }, createElement(StateProbe)),
  );
}

describe("Sidebar hydration", () => {
  // SSR output must depend ONLY on the cookie-seeded `defaultOpen`, never on the
  // viewport — the viewport hooks return their server snapshot (false) and the
  // auto-collapse effect runs post-mount. That keeps the server markup stable
  // and free of server-side `window` access. (`window.matchMedia` is
  // intentionally NOT mocked — SSR must not touch it at all.)
  //
  // It does NOT promise zero client FOUC: a narrow-viewport client still renders
  // expanded first, then the post-mount effect collapses it — an accepted
  // trade-off (the cookie spares the wide-screen majority).
  it("renders open-state purely from defaultOpen, without branching on viewport", () => {
    expect(ssr(true)).toContain(">expanded<");
    expect(ssr(false)).toContain(">collapsed<");
  });
});
