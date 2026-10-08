import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { SidebarProvider, useSidebar } from "@/shared/ui/kit/sidebar";

// jsdom implements neither `matchMedia` nor live `innerWidth` resizing. The
// sidebar's auto-collapse keys off both (`useIsMobile` < 768, `useIsNarrowViewport`
// < 1024), so we install a controllable mock and drive the viewport by hand.
const mediaListeners = new Set<() => void>();

function installMatchMedia() {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    writable: true,
    value: (query: string) => {
      const match = query.match(/max-width:\s*(\d+)px/);
      const maxWidth = match ? Number(match[1]) : Number.POSITIVE_INFINITY;
      return {
        get matches() {
          return window.innerWidth <= maxWidth;
        },
        media: query,
        onchange: null,
        addEventListener: (_type: string, cb: () => void) => mediaListeners.add(cb),
        removeEventListener: (_type: string, cb: () => void) => mediaListeners.delete(cb),
        addListener: (cb: () => void) => mediaListeners.add(cb),
        removeListener: (cb: () => void) => mediaListeners.delete(cb),
        dispatchEvent: () => false,
      } as unknown as MediaQueryList;
    },
  });
}

function setInnerWidth(width: number) {
  Object.defineProperty(window, "innerWidth", { configurable: true, writable: true, value: width });
}

/** Mount-time width — set BEFORE render so the first snapshot is correct. */
function withWidth(width: number) {
  setInnerWidth(width);
}

/** Post-mount resize — updates width and notifies the external store. */
function resizeTo(width: number) {
  setInnerWidth(width);
  act(() => {
    mediaListeners.forEach((cb) => cb());
  });
}

function clearSidebarCookie() {
  document.cookie = "sidebar_state=; path=/; max-age=0";
}

function Probe() {
  const { state, canExpand, toggleSidebar, setOpen } = useSidebar();
  return (
    <div>
      <span data-testid="state">{state}</span>
      <span data-testid="canExpand">{String(canExpand)}</span>
      <button data-testid="toggle" onClick={() => toggleSidebar()}>
        toggle
      </button>
      <button data-testid="open" onClick={() => setOpen(true)}>
        open
      </button>
      <button data-testid="close" onClick={() => setOpen(false)}>
        close
      </button>
    </div>
  );
}

function renderSidebar(defaultOpen = true) {
  return render(
    <SidebarProvider defaultOpen={defaultOpen}>
      <Probe />
    </SidebarProvider>,
  );
}

const WIDE = 1440;
const NARROW = 900; // desktop, below the 1024 auto-collapse breakpoint
const MOBILE = 500;

const state = () => screen.getByTestId("state").textContent;
const canExpand = () => screen.getByTestId("canExpand").textContent;

beforeEach(() => {
  installMatchMedia();
  setInnerWidth(WIDE);
  clearSidebarCookie();
});

afterEach(() => {
  cleanup();
  mediaListeners.clear();
  clearSidebarCookie();
});

describe("SidebarProvider — open-state persistence", () => {
  it("respects defaultOpen=true on a wide screen", () => {
    withWidth(WIDE);
    renderSidebar(true);
    expect(state()).toBe("expanded");
    expect(canExpand()).toBe("true");
  });

  // The key regression: a remount on cross-section navigation seeds defaultOpen
  // from the cookie. A collapsed preference must NOT be force-opened on mount.
  it("respects a collapsed preference (defaultOpen=false) and never auto-opens on mount", () => {
    withWidth(WIDE);
    renderSidebar(false);
    expect(state()).toBe("collapsed");
    expect(canExpand()).toBe("true");
  });
});

describe("SidebarProvider — auto-collapse (last resort)", () => {
  it("auto-collapses to the icon rail on a narrow desktop viewport", () => {
    withWidth(NARROW);
    renderSidebar(true);
    expect(state()).toBe("collapsed");
    expect(canExpand()).toBe("false");
  });

  it("does not auto-collapse the desktop state on mobile (Sheet handles it)", () => {
    withWidth(MOBILE);
    renderSidebar(true);
    // Mobile uses the Sheet; the desktop open-state is left at its default.
    expect(state()).toBe("expanded");
    expect(canExpand()).toBe("false");
  });

  it("collapses when crossing into narrow and restores the preference when widening", () => {
    withWidth(WIDE);
    renderSidebar(true);
    expect(state()).toBe("expanded");

    resizeTo(NARROW);
    expect(state()).toBe("collapsed");

    resizeTo(WIDE);
    expect(state()).toBe("expanded"); // wide preference (open) restored
  });
});

describe("SidebarProvider — manual control + preference", () => {
  it("remembers a manual collapse on wide across a narrow round-trip", () => {
    withWidth(WIDE);
    renderSidebar(true);

    fireEvent.click(screen.getByTestId("toggle"));
    expect(state()).toBe("collapsed");

    resizeTo(NARROW);
    expect(state()).toBe("collapsed");

    resizeTo(WIDE);
    // The user chose collapsed on a wide screen — widening must NOT re-open it.
    expect(state()).toBe("collapsed");
  });

  it("persists the wide-screen choice to the cookie", () => {
    withWidth(WIDE);
    renderSidebar(true);

    fireEvent.click(screen.getByTestId("close"));
    expect(document.cookie).toContain("sidebar_state=false");

    fireEvent.click(screen.getByTestId("open"));
    expect(document.cookie).toContain("sidebar_state=true");
  });

  it("lets the user peek the sidebar open on a narrow screen without persisting it", () => {
    withWidth(NARROW);
    renderSidebar(true);
    expect(state()).toBe("collapsed");

    clearSidebarCookie();
    fireEvent.click(screen.getByTestId("toggle"));
    expect(state()).toBe("expanded"); // peek works
    // Transient narrow-screen state must not corrupt the persisted preference.
    expect(document.cookie).not.toContain("sidebar_state=");
  });
});
