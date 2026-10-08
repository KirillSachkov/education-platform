"use client";

import { useRef } from "react";

export function TestimonialsCarousel({ children }: { children: React.ReactNode }) {
  const scrollRef = useRef<HTMLDivElement>(null);

  const scroll = (dir: "left" | "right") => {
    const el = scrollRef.current;
    if (!el) return;
    const cards = el.querySelectorAll<HTMLElement>("[data-card]");
    if (!cards.length) return;
    const cardWidth = cards[0].offsetWidth;
    const gap = 24;
    el.scrollBy({ left: dir === "right" ? cardWidth + gap : -(cardWidth + gap), behavior: "smooth" });
  };

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6">
      <div className="flex items-center gap-3 lg:gap-4">
        {/* Left arrow — desktop only */}
        <button
          type="button"
          onClick={() => scroll("left")}
          className="hidden shrink-0 items-center justify-center rounded-full border border-white/[0.08] bg-[#141416] p-2.5 text-white/40 shadow-lg shadow-black/30 transition-all hover:border-white/20 hover:text-white lg:flex"
          aria-label="Предыдущий отзыв"
        >
          <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M15 18l-6-6 6-6" />
          </svg>
        </button>

        {/* Cards */}
        <div
          ref={scrollRef}
          className="flex min-w-0 flex-1 gap-5 overflow-x-auto scrollbar-none lg:gap-6 snap-x snap-mandatory overscroll-x-contain [&>*]:snap-start"
        >
          {children}
        </div>

        {/* Right arrow — desktop only */}
        <button
          type="button"
          onClick={() => scroll("right")}
          className="hidden shrink-0 items-center justify-center rounded-full border border-white/[0.08] bg-[#141416] p-2.5 text-white/40 shadow-lg shadow-black/30 transition-all hover:border-white/20 hover:text-white lg:flex"
          aria-label="Следующий отзыв"
        >
          <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <path d="M9 18l6-6-6-6" />
          </svg>
        </button>
      </div>
    </div>
  );
}
