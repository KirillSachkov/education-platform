"use client";

import { useEffect, useState } from "react";
import { consultationLink } from "../config";
import { useReducedMotion } from "../hooks/use-reduced-motion";

/**
 * Floating Telegram consultation button. При первом маунте на 6 сек включается
 * pulsing ring («сигнал — нажми меня»), затем затихает. После — обычная кнопка
 * с hover-эффектом.
 *
 * Pulsing animation: GPU-accelerated (transform + opacity), 0 layout work.
 * При reduced-motion — ring отключён.
 */
export function FloatingTelegram() {
  const reduced = useReducedMotion();
  const [pulsing, setPulsing] = useState(() => !reduced);

  useEffect(() => {
    if (reduced) return;
    const timer = setTimeout(() => setPulsing(false), 6000);
    return () => clearTimeout(timer);
  }, [reduced]);

  return (
    <a
      href={consultationLink}
      data-growth-cta="floating_consultation"
      data-growth-placement="other"
      aria-label="Консультация в Telegram"
      target="_blank"
      rel="noopener noreferrer"
      className="group fixed bottom-6 right-6 z-50 flex items-center gap-2 rounded-full bg-[#6BADA5] px-5 py-3 text-sm font-medium text-[#0A0A0B] shadow-lg shadow-[#6BADA5]/20 transition-all hover:scale-105 hover:bg-[#5CEAC9] hover:shadow-[#6BADA5]/30 sm:bottom-8 sm:right-8"
    >
      {/* Pulsing ring — only first 6 seconds */}
      {pulsing && (
        <>
          <span
            className="pointer-events-none absolute inset-0 -z-10 rounded-full bg-[#6BADA5]/40"
            style={{ animation: "ftgPing 1.6s cubic-bezier(0, 0, 0.2, 1) infinite" }}
            aria-hidden="true"
          />
          <span
            className="pointer-events-none absolute inset-0 -z-10 rounded-full bg-[#6BADA5]/20"
            style={{
              animation: "ftgPing 1.6s cubic-bezier(0, 0, 0.2, 1) infinite",
              animationDelay: "0.5s",
            }}
            aria-hidden="true"
          />
          <style>{`
            @keyframes ftgPing {
              0%   { transform: scale(1);    opacity: 0.6; }
              80%  { transform: scale(1.6);  opacity: 0;   }
              100% { transform: scale(1.6);  opacity: 0;   }
            }
          `}</style>
        </>
      )}

      <svg
        className="relative h-5 w-5"
        viewBox="0 0 24 24"
        fill="currentColor"
        aria-hidden="true"
        focusable="false"
      >
        <path d="M11.944 0A12 12 0 0 0 0 12a12 12 0 0 0 12 12 12 12 0 0 0 12-12A12 12 0 0 0 12 0a12 12 0 0 0-.056 0zm4.962 7.224c.1-.002.321.023.465.14a.506.506 0 0 1 .171.325c.016.093.036.306.02.472-.18 1.898-.962 6.502-1.36 8.627-.168.9-.499 1.201-.82 1.23-.696.065-1.225-.46-1.9-.902-1.056-.693-1.653-1.124-2.678-1.8-1.185-.78-.417-1.21.258-1.91.177-.184 3.247-2.977 3.307-3.23.007-.032.014-.15-.056-.212s-.174-.041-.249-.024c-.106.024-1.793 1.14-5.061 3.345-.48.33-.913.49-1.302.48-.428-.008-1.252-.241-1.865-.44-.752-.245-1.349-.374-1.297-.789.027-.216.325-.437.893-.663 3.498-1.524 5.83-2.529 6.998-3.014 3.332-1.386 4.025-1.627 4.476-1.635z" />
      </svg>
      <span className="relative hidden sm:inline">Консультация</span>
    </a>
  );
}
