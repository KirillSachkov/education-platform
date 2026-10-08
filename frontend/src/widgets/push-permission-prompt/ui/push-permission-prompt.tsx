"use client";

import { useEffect, useState } from "react";
import { usePushSubscription } from "@/features/web-push";
import { usePwaPlatform } from "@/shared/lib/pwa";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";

const DISMISS_KEY = "push-prompt-dismissed-at";
const SUPPRESSION_MS = 1000 * 60 * 60 * 24 * 30; // 30 days
const REVEAL_DELAY_MS = 3500;

function isSuppressed(): boolean {
  try {
    const raw = window.localStorage.getItem(DISMISS_KEY);
    if (!raw) return false;
    const at = Number.parseInt(raw, 10);
    if (Number.isNaN(at)) return false;
    return Date.now() - at < SUPPRESSION_MS;
  } catch {
    // Storage blocked (iOS private mode) — fail closed so we don't pester.
    return true;
  }
}

function suppress(): void {
  try {
    window.localStorage.setItem(DISMISS_KEY, String(Date.now()));
  } catch {
    // ignore — same reason as above
  }
}

/**
 * Soft pre-prompt предлагающий включить push-уведомления — показывается ОДИН раз
 * при первом заходе на /home на мобильном устройстве (#342).
 *
 * Mounted внутри authenticated-ветки `home-client.tsx` → никогда не появляется на
 * публичном лендинге `/` (там анонимный трафик + server-redirect авторизованных на /home)
 * и вообще вне платформы.
 *
 * Платформенная логика:
 *   - iOS требует установленного PWA (standalone) — в Safari-вкладке push недоступен;
 *     показываем инструкцию «добавь на экран Домой», а не запрос разрешения.
 *   - Android / установленный iOS-PWA: soft pre-prompt → по клику нативный запрос.
 *   - Показываем только когда разрешение ещё не выдано/не отклонено (`default`).
 *   - localStorage-suppression на 30 дней после dismiss/enable — не спамим.
 */
export function PushPermissionPrompt() {
  const { isMobile, isIos, isStandalone } = usePwaPlatform();
  const { isSupported, permission, isSubscribed, isPending, subscribe } = usePushSubscription();
  const [show, setShow] = useState(false);

  // iOS без установки не умеет web-push — сперва на экран Домой.
  const iosNeedsInstall = isIos && !isStandalone;

  useEffect(() => {
    if (!isMobile) return;
    if (isSuppressed()) return;
    const handle = window.setTimeout(() => setShow(true), REVEAL_DELAY_MS);
    return () => window.clearTimeout(handle);
  }, [isMobile]);

  if (!show || isSubscribed) return null;

  // На устройствах, где push реально доступен, показываем только при неопределённом
  // разрешении (granted-но-без-подписки и denied не трогаем — это работа настроек).
  if (!iosNeedsInstall && (!isSupported || permission !== "default")) return null;

  function handleDismiss() {
    suppress();
    setShow(false);
  }

  async function handleEnable() {
    const ok = await subscribe();
    // Закрываем при любом терминальном исходе (включили / отклонили) и больше не спамим.
    if (ok || permission === "denied") {
      suppress();
      setShow(false);
    }
  }

  return (
    <div
      role="region"
      aria-labelledby="push-prompt-title"
      className={cn(
        "md:hidden",
        "fixed inset-x-3 z-50",
        "rounded-2xl border border-border/60 bg-card shadow-2xl shadow-black/30",
        "p-4",
        // Над mobile-bottom-nav (56px) + safe-area.
        "bottom-[calc(env(safe-area-inset-bottom)+72px)]",
      )}
    >
      <div className="flex items-start gap-3">
        <span
          aria-hidden="true"
          className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary/12 text-primary"
        >
          <Icons.notification className="size-5" />
        </span>
        <div className="min-w-0 flex-1">
          <h2 id="push-prompt-title" className="text-sm font-semibold leading-snug">
            Включить уведомления?
          </h2>
          {iosNeedsInstall ? (
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Чтобы получать push на iPhone, сначала добавьте приложение на экран Домой: нажмите{" "}
              <span className="inline-flex items-center gap-0.5 align-middle text-foreground">
                <Icons.shareIos className="size-3.5" />
              </span>{" "}
              «Поделиться» → «На экран Домой», затем откройте его и включите уведомления.
            </p>
          ) : (
            <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
              Ответы проверяющего, новые материалы и важные объявления — прямо на экран
              устройства, даже когда вкладка закрыта.
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={handleDismiss}
          aria-label="Закрыть подсказку"
          className="-mr-1 -mt-1 inline-flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
        >
          <Icons.close className="size-4" />
        </button>
      </div>

      {!iosNeedsInstall && (
        <div className="mt-3 flex justify-end gap-2">
          <Button size="sm" variant="ghost" onClick={handleDismiss} disabled={isPending}>
            Не сейчас
          </Button>
          <Button
            size="sm"
            onClick={() => {
              void handleEnable();
            }}
            disabled={isPending}
          >
            {isPending && <Icons.loading className="mr-1.5 size-3.5 animate-spin" />}
            Включить
          </Button>
        </div>
      )}
    </div>
  );
}
