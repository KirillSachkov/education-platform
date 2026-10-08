"use client";

import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { usePathname } from "next/navigation";
import { useState, type ReactNode } from "react";
import { resolveActiveTabIndex } from "../lib/active-tab";

interface NavState {
  direction: 1 | -1 | 0;
  lastPath: string;
  lastIndex: number;
}

/**
 * Wraps the platform content area in a tab-aware AnimatePresence. The page
 * slides horizontally only when the **active bottom-nav tab** changes — opening
 * a material from inside a course (same tab) cross-fades instead, avoiding
 * jarring jumps on intra-section navigation.
 *
 * `mode="popLayout"` is intentional: it gives the exiting motion.div
 * `position: absolute` so the new page can occupy the flow slot while both
 * animate over each other. The default `mode="sync"` would leave both children
 * in normal flow and stack them vertically for the duration of the tween —
 * exactly the flicker we want to avoid.
 */
export function MobileTabTransition({ children }: { children: ReactNode }) {
  const pathname = usePathname() ?? "/";
  const reduceMotion = useReducedMotion();
  const activeIndex = resolveActiveTabIndex(pathname);

  // "Set state during render" is the React-blessed way to derive a value from
  // a changing prop without `useEffect`. We compute the slide direction once
  // per path change and persist it via state so the AnimatePresence below sees
  // a stable value for the in/out tween.
  const [state, setState] = useState<NavState>({
    direction: 0,
    lastPath: pathname,
    lastIndex: activeIndex,
  });

  if (state.lastPath !== pathname) {
    const direction =
      activeIndex !== -1 && state.lastIndex !== -1 && activeIndex !== state.lastIndex
        ? activeIndex > state.lastIndex
          ? 1
          : -1
        : 0;
    setState({ direction, lastPath: pathname, lastIndex: activeIndex });
  }

  if (reduceMotion) {
    return <>{children}</>;
  }

  return (
    <AnimatePresence mode="popLayout" initial={false}>
      <motion.div
        key={pathname}
        initial={{ opacity: 0, x: state.direction * 16 }}
        animate={{ opacity: 1, x: 0 }}
        exit={{ opacity: 0, x: state.direction * -16 }}
        transition={{ duration: 0.22, ease: [0.22, 0.61, 0.36, 1] }}
      >
        {children}
      </motion.div>
    </AnimatePresence>
  );
}
