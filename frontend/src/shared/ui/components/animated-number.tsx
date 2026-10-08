"use client";

import { useEffect, useRef } from "react";
import { cn } from "@/shared/lib/css";

interface AnimatedNumberProps {
  value: number;
  /** Format the number to a display string. Defaults to `toLocaleString()`. */
  format?: (value: number) => string;
  className?: string;
  /** Animate on the first render too. Default: only on subsequent changes. */
  animateOnMount?: boolean;
}

const defaultFormat = (value: number) => value.toLocaleString();

/**
 * transitions.dev "number pop-in" — each character re-enters with a blurred
 * slide when the value changes. Digits rest fully visible; the pop-in replays
 * only on change (or on mount if `animateOnMount`), so it never sticks blank.
 * The `prefers-reduced-motion` guard in globals.css disables the motion.
 */
export function AnimatedNumber({
  value,
  format = defaultFormat,
  className,
  animateOnMount = false,
}: AnimatedNumberProps) {
  const groupRef = useRef<HTMLSpanElement>(null);
  const isFirst = useRef(true);
  const text = format(value);

  useEffect(() => {
    const group = groupRef.current;
    if (!group) return;
    if (isFirst.current && !animateOnMount) {
      isFirst.current = false;
      return;
    }
    isFirst.current = false;
    // Remove → reflow → re-add so the keyframes restart from offset 0.
    group.classList.remove("is-animating");
    void group.offsetHeight;
    group.classList.add("is-animating");
  }, [text, animateOnMount]);

  const chars = [...text];
  const len = chars.length;

  return (
    <span ref={groupRef} className={cn("t-digit-group", className)} aria-label={text}>
      {chars.map((ch, i) => (
        <span
          key={`${i}-${ch}`}
          className="t-digit"
          aria-hidden
          data-stagger={i === len - 2 ? "1" : i === len - 1 ? "2" : undefined}
        >
          {ch}
        </span>
      ))}
    </span>
  );
}
