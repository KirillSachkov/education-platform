"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Icons } from "@/shared/ui/icons";
import { useLevelUpCelebration } from "../model/use-level-up-celebration";

/**
 * Празднование повышения уровня (#555): модалка с конфетти в момент level-up.
 *
 * Монтируется один раз в глобальном shell (`AppLayout`) для авторизованных. Триггерится
 * приходом «свежего» непразднованного UserLeveledUp-уведомления по SSE (см.
 * {@link useLevelUpCelebration}). Конфетти — `canvas-confetti`, динамический импорт на открытие,
 * пропускается при `prefers-reduced-motion`. Доступность (focus-trap, ESC, scroll-lock, aria) —
 * за Radix Dialog.
 */
export function LevelUpCelebration() {
  const { celebration, dismiss } = useLevelUpCelebration();
  const router = useRouter();

  const open = celebration !== null;
  const celebratedId = celebration?.notificationId;

  // Конфетти — imperative side-effect на открытие модалки. Keyed на id, чтобы
  // выстрелить ровно один раз на каждое поздравление. Reduced-motion → без конфетти.
  useEffect(() => {
    if (!celebratedId) return;
    if (typeof window === "undefined") return;
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)").matches) return;

    let cancelled = false;
    void (async () => {
      try {
        const confetti = (await import("canvas-confetti")).default;
        if (cancelled) return;
        const fire = (particleRatio: number, opts: Record<string, unknown>) =>
          confetti({
            origin: { y: 0.35 },
            particleCount: Math.floor(180 * particleRatio),
            ...opts,
          });
        fire(0.25, { spread: 26, startVelocity: 55 });
        fire(0.35, { spread: 60 });
        fire(0.2, { spread: 100, decay: 0.91, scalar: 0.8 });
        fire(0.2, { spread: 120, startVelocity: 25, decay: 0.92, scalar: 1.2 });
      } catch {
        // confetti — украшение; падение динамического импорта не должно ломать модалку.
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [celebratedId]);

  function handleOpenChange(next: boolean) {
    if (!next) dismiss();
  }

  function handleContinue() {
    dismiss();
    router.push(routes.home);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-sm text-center">
        <DialogHeader className="items-center gap-3">
          <span className="flex size-16 items-center justify-center rounded-full bg-amber-500/15 text-amber-500">
            <Icons.trophy size={34} />
          </span>
          <DialogTitle className="text-2xl">
            Новый уровень — {celebration?.newLevel ?? ""}!
          </DialogTitle>
          <DialogDescription className="text-base">
            {celebration && celebration.totalXp > 0
              ? `Ты уже набрал ${celebration.totalXp} XP. `
              : ""}
            Так держать — продолжай учиться и расти!
          </DialogDescription>
        </DialogHeader>
        <DialogFooter className="sm:justify-center">
          <Button onClick={handleContinue} className="min-w-44">
            Продолжить учиться
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
