import { useSyncExternalStore } from "react";

/**
 * Boolean state — пользователь проскроллил страницу ниже порога. Подписывается
 * напрямую на `window.scroll` через `useSyncExternalStore`, чтобы не плодить
 * `useState + useEffect`-связку, которую ругает React Compiler
 * (`set-state-in-effect`).
 *
 * `getServerSnapshot` возвращает `false` — на SSR scrollY = 0, header
 * рендерится в "top of page" режиме до hydration.
 */
export function useIsScrolled(threshold = 10): boolean {
  return useSyncExternalStore(
    (callback) => {
      window.addEventListener("scroll", callback, { passive: true });
      return () => window.removeEventListener("scroll", callback);
    },
    () => window.scrollY > threshold,
    () => false,
  );
}
