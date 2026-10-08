"use client";

import type { CSSProperties } from "react";
import { useEffect, useEffectEvent, useRef, useState } from "react";

/**
 * Tracks the horizontal scroll position of a container and keeps the active
 * child centred. Powers the edge-fade affordance + auto-scroll-into-view for
 * the mobile section tab bars (`AppSectionTopTabs`, `CourseTopTabs`,
 * `CourseBuilderMobileTabs`) and the admin-overview tablist.
 *
 * Those bars pack 6–8 tabs into a ~364px strip with `overflow-x: auto` +
 * `scrollbar-none`. They scroll fine, but gave the user no cue that more tabs
 * exist, and the active tab could land off-screen — so sections read as
 * "missing / unreachable" on mobile (issue #401).
 *
 * Returns a `ref` for the scroll container plus `atStart` / `atEnd` flags
 * (feed them to {@link scrollFadeMask}). Whenever `activeKey` changes, the
 * element marked `data-active="true"` (or Radix `data-state="active"`) is
 * scrolled to the horizontal centre — vertical position is never touched.
 */
export function useScrollAffordance<T extends HTMLElement>(activeKey?: unknown) {
  const ref = useRef<T>(null);
  const [atStart, setAtStart] = useState(true);
  const [atEnd, setAtEnd] = useState(true);

  const sync = useEffectEvent(() => {
    const el = ref.current;
    if (!el) return;
    const max = el.scrollWidth - el.clientWidth;
    setAtStart(el.scrollLeft <= 1);
    // max <= 0 ⇒ nothing overflows ⇒ treat as fully at the end (no fade).
    setAtEnd(max <= 0 || el.scrollLeft >= max - 1);
  });

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const onScroll = () => sync();
    sync();
    el.addEventListener("scroll", onScroll, { passive: true });
    const observer = new ResizeObserver(() => sync());
    observer.observe(el);
    return () => {
      el.removeEventListener("scroll", onScroll);
      observer.disconnect();
    };
  }, []);

  // Centre the active tab whenever the active key changes. Deferred to the next
  // frame so it runs after layout — measuring synchronously on mount yielded a
  // pre-layout rect and the scroll never landed (verified — scrollLeft stayed
  // 0). Consumers include the visible-tab count in `activeKey` so this re-runs
  // when async-gated tabs (roles/access) finally render.
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const frame = requestAnimationFrame(() => {
      const active =
        el.querySelector<HTMLElement>('[data-active="true"]') ??
        el.querySelector<HTMLElement>('[data-state="active"]');
      // `inline: center` scrolls only this container horizontally; `block:
      // nearest` leaves the page's vertical scroll untouched (verified).
      active?.scrollIntoView({ inline: "center", block: "nearest", behavior: "instant" });
    });
    return () => cancelAnimationFrame(frame);
  }, [activeKey]);

  return { ref, atStart, atEnd };
}

/** px width of the edge fade — wide enough to peek the next tab. */
const FADE_WIDTH = 28;

/**
 * Mask that fades the scroll container's content at whichever edge still has
 * off-screen content, signalling "swipe for more". Pair with
 * {@link useScrollAffordance}'s `atStart` / `atEnd`.
 */
export function scrollFadeMask(atStart: boolean, atEnd: boolean): CSSProperties {
  const left = atStart ? "0px" : `${FADE_WIDTH}px`;
  const right = atEnd ? "0px" : `${FADE_WIDTH}px`;
  const gradient = `linear-gradient(to right, transparent 0, #000 ${left}, #000 calc(100% - ${right}), transparent 100%)`;
  return { maskImage: gradient, WebkitMaskImage: gradient };
}
