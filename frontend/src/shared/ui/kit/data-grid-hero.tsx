"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { cn } from "@/shared/lib/css";

interface DataGridHeroProps {
  rows: number;
  cols: number;
  spacing?: number;
  duration?: number;
  color?: string;
  animationType?: "pulse" | "wave" | "random";
  pulseEffect?: boolean;
  mouseGlow?: boolean;
  opacityMin?: number;
  opacityMax?: number;
  background?: string;
  className?: string;
  children?: ReactNode;
}

export function DataGridHero({
  rows,
  cols,
  spacing = 4,
  duration = 5,
  color = "#6BADA5",
  animationType = "pulse",
  pulseEffect = true,
  mouseGlow = true,
  opacityMin = 0.05,
  opacityMax = 0.6,
  background = "#0A0A0B",
  className,
  children,
}: DataGridHeroProps) {
  const gridRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const container = gridRef.current;
    if (!container) return;

    const build = () => {
      container.replaceChildren();
      const w = container.clientWidth;
      const h = container.clientHeight;
      if (w === 0 || h === 0) return;

      // Reduce columns on narrow screens to keep cells visible
      const effectiveCols = w < 640 ? Math.min(cols, 14) : cols;

      // Compute square cell size from container width
      const colGaps = (effectiveCols - 1) * spacing;
      const cellSize = Math.floor((w - colGaps) / effectiveCols);

      // Compute rows needed to fill container height
      const rowsNeeded = Math.ceil((h + spacing) / (cellSize + spacing));
      const actualRows = Math.max(rows, rowsNeeded);

      container.style.gridTemplateColumns = `repeat(${effectiveCols}, ${cellSize}px)`;
      container.style.gridTemplateRows = "";
      container.style.gridAutoRows = `${cellSize}px`;
      container.style.gap = `${spacing}px`;
      container.style.setProperty("--mouse-glow-opacity", mouseGlow ? "1" : "0");

      const total = actualRows * effectiveCols;
      const centerRow = Math.floor(actualRows / 2);
      const centerCol = Math.floor(effectiveCols / 2);

      for (let i = 0; i < total; i++) {
        const cell = document.createElement("div");
        cell.className = "grid-cell";
        cell.style.backgroundColor = color;
        cell.style.setProperty("--opacity-min", String(opacityMin));
        cell.style.setProperty("--opacity-max", String(opacityMax));

        if (pulseEffect) {
          let delay: number;
          const r = Math.floor(i / effectiveCols);
          const c = i % effectiveCols;

          if (animationType === "wave") {
            delay = (r + c) * 0.1;
          } else if (animationType === "random") {
            delay = Math.random() * duration;
          } else {
            const dr = Math.abs(r - centerRow);
            const dc = Math.abs(c - centerCol);
            delay = Math.sqrt(dr * dr + dc * dc) * 0.2;
          }

          cell.style.animation = `cell-pulse ${duration}s infinite alternate`;
          cell.style.animationDelay = `${delay.toFixed(3)}s`;
        }

        container.appendChild(cell);
      }
    };

    build();

    const observer = new ResizeObserver(() => build());
    observer.observe(container);
    return () => {
      observer.disconnect();
      container.replaceChildren();
    };
  }, [cols, rows, spacing, color, animationType, pulseEffect, duration, opacityMin, opacityMax, mouseGlow]);

  useEffect(() => {
    const container = gridRef.current;
    if (!mouseGlow || !container) return;

    const handler = (e: MouseEvent) => {
      const rect = container.getBoundingClientRect();
      container.style.setProperty("--mouse-x", `${e.clientX - rect.left}px`);
      container.style.setProperty("--mouse-y", `${e.clientY - rect.top}px`);
    };

    window.addEventListener("mousemove", handler);
    return () => window.removeEventListener("mousemove", handler);
  }, [mouseGlow]);

  return (
    <div className={cn("data-grid-hero", className)} style={{ background }}>
      <div ref={gridRef} className="grid-container" aria-hidden="true" />
      <div
        className="dgh-vignette"
        style={{
          background: `radial-gradient(ellipse at center, transparent 50%, ${background} 95%)`,
        }}
        aria-hidden="true"
      />
      <div className="hero-content">
        {children}
      </div>
    </div>
  );
}
