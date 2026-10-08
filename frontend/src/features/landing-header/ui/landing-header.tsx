"use client";

import { useState } from "react";
import Link from "next/link";
import { cn } from "@/shared/lib/css";
import { useIsScrolled } from "@/shared/lib/react/use-is-scrolled";
import { LogoMark } from "@/shared/ui/kit/logo";
import { Icons } from "@/shared/ui/icons";

interface AnchorLink {
  label: string;
  sectionId: string;
}

interface LandingHeaderProps {
  anchorLinks?: AnchorLink[];
  ctaText?: string;
  ctaHref?: string;
}

export function LandingHeader({ anchorLinks = [], ctaText, ctaHref }: LandingHeaderProps) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const scrolled = useIsScrolled(10);

  return (
    <header
      className={cn(
        // sticky (not fixed): on iOS Safari a `position: fixed; top: 0` header
        // detaches from the visual viewport while the address bar collapses on
        // scroll, leaving a gap above it. Sticky tracks the visual viewport.
        // The header still overlays the hero — `main` gets `-mt-14` to pull
        // content back under it (see (landing)/layout.tsx).
        "sticky top-0 z-50 border-b transition-all duration-300",
        scrolled
          ? "border-white/[0.06] bg-[#0A0A0B]/80 backdrop-blur-2xl"
          : "border-transparent bg-transparent",
      )}
    >
      <div className="mx-auto max-w-7xl px-6 flex h-14 items-center justify-between">
        {/* LEFT: Logo */}
        <Link href="/" className="flex items-center gap-2 shrink-0">
          <LogoMark size={24} />
          <span className="text-sm font-semibold tracking-tight text-white">SachkovLearn</span>
        </Link>

        {/* CENTER: Anchor links — desktop only */}
        {anchorLinks.length > 0 && (
          <nav className="hidden lg:flex items-center gap-1">
            {anchorLinks.map((link) => (
              <a
                key={link.sectionId}
                href={`#${link.sectionId}`}
                className="rounded-md px-3 py-1.5 text-[13px] text-white/40 transition-colors hover:text-[#6BADA5] hover:bg-white/[0.03]"
              >
                {link.label}
              </a>
            ))}
          </nav>
        )}

        {/* RIGHT: CTA / Login / In-platform link + burger */}
        <div className="flex items-center gap-2">
          {ctaText && ctaHref ? (
            <Link
              href={ctaHref}
              data-growth-cta="header_primary"
              data-growth-placement="header"
              className="hidden sm:inline-flex items-center rounded-lg bg-[#6BADA5] px-4 py-1.5 text-[13px] font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9]"
            >
              {ctaText}
            </Link>
          ) : null}

          <button
            className="md:hidden p-1.5 rounded-md text-white/50 hover:text-white hover:bg-white/[0.06] transition-colors"
            onClick={() => setMobileOpen((prev) => !prev)}
            aria-label={mobileOpen ? "Закрыть меню" : "Открыть меню"}
          >
            {mobileOpen ? <Icons.close className="h-5 w-5" /> : <Icons.menu className="h-5 w-5" />}
          </button>
        </div>
      </div>

      {/* Mobile menu */}
      {mobileOpen && (
        <div className="absolute inset-x-0 top-full md:hidden border-t border-white/[0.06] bg-[#0A0A0B]/95 backdrop-blur-2xl px-6 py-4 space-y-1">
          {anchorLinks.length > 0 && (
            <div className="space-y-1">
              {anchorLinks.map((link) => (
                <a
                  key={link.sectionId}
                  href={`#${link.sectionId}`}
                  className="block rounded-lg px-3 py-2 text-sm text-white/40 transition-colors hover:text-[#6BADA5] hover:bg-white/[0.04]"
                  onClick={() => setMobileOpen(false)}
                >
                  {link.label}
                </a>
              ))}
            </div>
          )}
          {ctaText && ctaHref && (
            <div className="pt-2 border-t border-white/[0.06] mt-2">
              <Link
                href={ctaHref}
                data-growth-cta="header_primary"
                data-growth-placement="header"
                className="block w-full rounded-lg bg-[#6BADA5] px-4 py-2.5 text-center text-sm font-medium text-[#0A0A0B] transition-all hover:bg-[#5CEAC9]"
                onClick={() => setMobileOpen(false)}
              >
                {ctaText}
              </Link>
            </div>
          )}
        </div>
      )}
    </header>
  );
}
