import { renderHook, type RenderHookResult } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useHashScroll } from "../use-hash-scroll";

// Хэш ставим через replaceState — он, в отличие от `location.hash = …`, НЕ фолбэчит
// jsdom-овый `hashchange` (иначе спурьёзное событие переназначало бы таймеры и ломало
// проверки счётчика/abort). Реальные hashchange в тестах эмулируем явным dispatchEvent.
function setHash(value: string) {
  window.history.replaceState(null, "", value);
}

describe("useHashScroll", () => {
  let scrollIntoView: ReturnType<typeof vi.fn>;
  let current: RenderHookResult<void, unknown> | null = null;

  beforeEach(() => {
    scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView as unknown as Element["scrollIntoView"];
    vi.useFakeTimers();
    setHash("#");
    document.body.replaceChildren();
    current = null;
  });

  afterEach(() => {
    // Размонтируем — иначе effect-cleanup (removeEventListener + clearTimers) не отработает
    // и слушатели/таймеры протекут в соседние тесты.
    current?.unmount();
    current = null;
    vi.clearAllTimers();
    vi.useRealTimers();
    setHash("#");
    document.body.replaceChildren();
  });

  function addAnchor(id: string) {
    const el = document.createElement("div");
    el.id = id;
    document.body.appendChild(el);
  }

  function mount(enabled = true) {
    current = renderHook(() => useHashScroll(enabled));
  }

  it("scrolls to the hashed element on mount, honoring scroll-margin", () => {
    addAnchor("plan-a");
    setHash("#plan-a");
    mount();
    expect(scrollIntoView).toHaveBeenCalledWith({ block: "start" });
  });

  it("re-aligns on the timer checkpoints as lazy content settles", () => {
    addAnchor("plan-a");
    setHash("#plan-a");
    mount();
    scrollIntoView.mockClear();
    vi.advanceTimersByTime(700);
    expect(scrollIntoView).toHaveBeenCalledTimes(4); // 80/200/400/700ms
  });

  it("does nothing when there is no hash", () => {
    mount();
    vi.advanceTimersByTime(700);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it("does nothing when disabled", () => {
    addAnchor("plan-a");
    setHash("#plan-a");
    mount(false);
    vi.advanceTimersByTime(700);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it("aborts pending re-alignment on a real user gesture (no yank-back)", () => {
    addAnchor("plan-a");
    setHash("#plan-a");
    mount();
    scrollIntoView.mockClear();
    window.dispatchEvent(new Event("wheel"));
    vi.advanceTimersByTime(700);
    expect(scrollIntoView).not.toHaveBeenCalled();
  });

  it("re-scrolls when the hash changes (plan-to-plan navigation)", () => {
    addAnchor("plan-a");
    setHash("#plan-a");
    mount();
    scrollIntoView.mockClear();
    addAnchor("plan-b");
    setHash("#plan-b");
    window.dispatchEvent(new Event("hashchange"));
    expect(scrollIntoView).toHaveBeenCalled();
  });
});
