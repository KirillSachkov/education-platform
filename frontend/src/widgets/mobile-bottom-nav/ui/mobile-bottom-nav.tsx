"use client";

import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { cn } from "@/shared/lib/css";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { BOTTOM_NAV_TABS, type BottomNavTabId, resolveActiveTabIndex } from "../lib/active-tab";
import { MobileMenuSheet } from "./mobile-menu-sheet";

// «Главная» (центр) рендерится фирменным LogoMark в акцентном круге, поэтому
// иконки заведены только для четырёх остальных вкладок. «Меню» — action-вкладка:
// открывает шторку со всеми разделами (см. MobileMenuSheet), а не навигирует.
const ICONS: Record<Exclude<BottomNavTabId, "home">, IconComponent> = {
  saved: Icons.bookmark,
  menu: Icons.menu,
};

export function MobileBottomNav() {
  const pathname = usePathname() ?? "/";
  const reduceMotion = useReducedMotion();
  const activeIndex = resolveActiveTabIndex(pathname);
  const [menuOpen, setMenuOpen] = useState(false);
  // Pill всегда один (общий layoutId): при открытой шторке он переезжает под
  // «Меню» и возвращается на route-активную вкладку после закрытия. Рендерить
  // два pill'а одновременно нельзя — конфликт layoutId в framer-motion.
  const menuIndex = BOTTOM_NAV_TABS.findIndex((tab) => tab.id === "menu");
  const pillIndex = menuOpen ? menuIndex : activeIndex;

  return (
    <>
      <nav
        aria-label="Главное меню"
        className={cn(
          "md:hidden",
          "fixed inset-x-0 bottom-0 z-40",
          "bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80",
          "border-t border-border/50",
        )}
        style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
      >
        <ul className="mx-auto flex max-w-md items-stretch justify-around">
          {BOTTOM_NAV_TABS.map((tab, index) => {
            const active = index === activeIndex;

            const Icon = tab.id === "home" ? Icons.course : ICONS[tab.id];
            const isMenuTab = tab.id === "menu";
            // «Меню» подсвечивается и когда открыта шторка, и когда юзер на одном
            // из «шторочных» маршрутов (active по isMenuSection).
            const highlighted = active || (isMenuTab && menuOpen);

            const cellClass = cn(
              "relative flex h-14 min-h-[44px] w-full flex-col items-center justify-center gap-0.5",
              "text-[11px] leading-none transition-colors",
              highlighted ? "text-primary" : "text-muted-foreground hover:text-foreground",
            );
            const cellContent = (
              <>
                <span className="relative">
                  <Icon className="size-[22px]" strokeWidth={highlighted ? 2.4 : 1.8} />
                </span>
                <span className={cn("font-medium", highlighted && "font-semibold")}>
                  {tab.label}
                </span>
              </>
            );

            return (
              <li key={tab.id} className="relative flex-1">
                <AnimatePresence initial={false}>
                  {index === pillIndex && (
                    <motion.span
                      layoutId={reduceMotion ? undefined : "bottom-nav-pill"}
                      aria-hidden
                      className="absolute inset-x-3 inset-y-1 rounded-2xl bg-primary/12"
                      transition={
                        reduceMotion
                          ? { duration: 0 }
                          : { type: "spring", stiffness: 380, damping: 32 }
                      }
                    />
                  )}
                </AnimatePresence>
                {isMenuTab ? (
                  <button
                    type="button"
                    onClick={() => setMenuOpen(true)}
                    aria-haspopup="dialog"
                    aria-expanded={menuOpen}
                    aria-controls="mobile-menu-sheet"
                    className={cellClass}
                  >
                    {cellContent}
                  </button>
                ) : (
                  <Link
                    href={tab.href!}
                    // Default Next.js prefetch (true) — вкладки бара это самые
                    // вероятные next-tap'ы, префетч срезает ~100-200ms INP.
                    aria-current={active ? "page" : undefined}
                    className={cellClass}
                  >
                    {cellContent}
                  </Link>
                )}
              </li>
            );
          })}
        </ul>
      </nav>
      <MobileMenuSheet open={menuOpen} onOpenChange={setMenuOpen} />
    </>
  );
}
