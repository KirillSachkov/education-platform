"use client";

import { useEffect, useEffectEvent, useRef, useState } from "react";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";

export function useCarouselRef() {
  return useRef<HTMLDivElement>(null);
}

export function CarouselButtons({
  scrollRef,
}: {
  scrollRef: React.RefObject<HTMLDivElement | null>;
}) {
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(true);

  const updateState = useEffectEvent(() => {
    const el = scrollRef.current;
    if (!el) return;
    setCanScrollLeft(el.scrollLeft > 10);
    setCanScrollRight(el.scrollLeft + el.clientWidth < el.scrollWidth - 10);
  });

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;

    const handleScroll = () => updateState();

    updateState();
    el.addEventListener("scroll", handleScroll, { passive: true });
    return () => el.removeEventListener("scroll", handleScroll);
  }, [scrollRef]);

  const scroll = (dir: "left" | "right") => {
    scrollRef.current?.scrollBy({
      left: dir === "left" ? -310 : 310,
      behavior: "smooth",
    });
  };

  return (
    <div className="flex gap-1 shrink-0">
      <Button
        variant="ghost"
        size="icon"
        className="size-8 rounded-full"
        onClick={() => scroll("left")}
        disabled={!canScrollLeft}
      >
        <Icons.chevronLeft className="size-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        className="size-8 rounded-full"
        onClick={() => scroll("right")}
        disabled={!canScrollRight}
      >
        <Icons.chevronRight className="size-4" />
      </Button>
    </div>
  );
}

export function CarouselRow({
  children,
  ref,
}: {
  children: React.ReactNode;
  ref: React.RefObject<HTMLDivElement | null>;
}) {
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const handler = (e: WheelEvent) => {
      if (el.scrollWidth <= el.clientWidth) return;
      if (e.deltaX !== 0) return;
      const atStart = el.scrollLeft <= 0;
      const atEnd = el.scrollLeft + el.clientWidth >= el.scrollWidth - 1;
      if ((atStart && e.deltaY < 0) || (atEnd && e.deltaY > 0)) return;
      e.preventDefault();
      el.scrollBy({ left: e.deltaY, behavior: "instant" });
    };
    el.addEventListener("wheel", handler, { passive: false });
    return () => el.removeEventListener("wheel", handler);
  }, [ref]);

  return (
    <div
      ref={ref}
      className="flex gap-3 overflow-x-auto scrollbar-none snap-x snap-mandatory overscroll-x-contain [&>*]:snap-start"
    >
      {children}
    </div>
  );
}
