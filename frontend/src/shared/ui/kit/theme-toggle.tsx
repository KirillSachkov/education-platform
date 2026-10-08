"use client";

import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { IconSwap } from "@/shared/ui/components/icon-swap";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";

/**
 * Tri-state toggle: dark → light → system → dark…
 * Icon reflects current selection (Moon / Sun / MonitorSmartphone).
 * For per-app settings page use `AppearanceSection` instead.
 */
export function ThemeToggle() {
  const { theme, setTheme } = useTheme();
  const mounted = useSyncExternalStore(
    () => () => {},
    () => true,
    () => false,
  );

  const current = (mounted ? (theme ?? "system") : "dark") as "light" | "dark" | "system";

  function cycle() {
    const next = current === "dark" ? "light" : current === "light" ? "system" : "dark";
    setTheme(next);
  }

  const label =
    current === "dark"
      ? "Тёмная тема — нажмите чтобы переключить на светлую"
      : current === "light"
        ? "Светлая тема — нажмите чтобы переключить на системную"
        : "Системная тема — нажмите чтобы переключить на тёмную";

  return (
    <Button
      variant="ghost"
      size="icon"
      className="size-8 rounded-xl"
      onClick={cycle}
      aria-label={label}
    >
      <IconSwap
        activeKey={current}
        className="text-muted-foreground"
        items={[
          { key: "dark", node: <Icons.themeDark size={15} /> },
          { key: "light", node: <Icons.themeLight size={15} /> },
          { key: "system", node: <Icons.themeSystem size={15} /> },
        ]}
      />
      <span className="sr-only">{label}</span>
    </Button>
  );
}
