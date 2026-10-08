"use client";

import { useEffect, useRef, useSyncExternalStore } from "react";
import { useSidebar } from "@/shared/ui/kit/sidebar";

const STORAGE_KEY = "course-theater-mode";
const CHANGE_EVENT = "course-theater-mode-change";

function subscribe(callback: () => void) {
  window.addEventListener("storage", callback);
  window.addEventListener(CHANGE_EVENT, callback);
  return () => {
    window.removeEventListener("storage", callback);
    window.removeEventListener(CHANGE_EVENT, callback);
  };
}

function getSnapshot() {
  return localStorage.getItem(STORAGE_KEY) === "true";
}

function getServerSnapshot() {
  return false;
}

export function useTheaterMode() {
  const { setOpen, isMobile } = useSidebar();
  const isTheaterMode = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  // `setOpen` из shadcn `useSidebar()` пересоздаётся на каждом рендере: если
  // его держать в deps useEffect'а ниже, эффект перезапустится после любого
  // ручного `toggleSidebar` и сразу вернёт sidebar в `!isTheaterMode`, ломая
  // отдельную кнопку «Свернуть». Берём через ref — sync срабатывает только
  // при реальном переключении theater-режима / mobile-флага.
  const setOpenRef = useRef(setOpen);
  useEffect(() => {
    setOpenRef.current = setOpen;
  });

  // Sync sidebar state with theater preference. Mobile uses a sheet,
  // not the desktop collapsible sidebar — leave it alone.
  useEffect(() => {
    if (isMobile) return;
    setOpenRef.current(!isTheaterMode);
  }, [isTheaterMode, isMobile]);

  const toggle = () => {
    const next = !getSnapshot();
    localStorage.setItem(STORAGE_KEY, String(next));
    window.dispatchEvent(new Event(CHANGE_EVENT));
  };

  return { isTheaterMode, toggle, isMobile };
}
