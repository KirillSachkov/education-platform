"use client";

import Link from "next/link";
import { useState, useSyncExternalStore } from "react";
import { Button } from "@/shared/ui/kit/button";
import { Switch } from "@/shared/ui/kit/switch";
import { useCookieConsent } from "@/shared/lib/use-cookie-consent";

const subscribeToHydration = () => () => {};
const getClientSnapshot = () => true;
const getServerSnapshot = () => false;

export function CookieBanner() {
  const { hasDecided, acceptAll, acceptNecessaryOnly, acceptCustom } = useCookieConsent();
  const isHydrated = useSyncExternalStore(
    subscribeToHydration,
    getClientSnapshot,
    getServerSnapshot,
  );
  const [isCustomizing, setIsCustomizing] = useState(false);
  const [analytics, setAnalytics] = useState(true);
  const [marketing, setMarketing] = useState(false);

  if (!isHydrated || hasDecided) return null;

  return (
    <div
      role="region"
      aria-label="Настройки cookies"
      aria-live="polite"
      className="fixed inset-x-0 bottom-0 z-50 px-2 pb-2 md:px-6 md:pb-6"
    >
      <div className="mx-auto max-w-5xl rounded-xl border border-border bg-background p-3 shadow-lg md:p-4">
        {!isCustomizing ? (
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p className="max-w-2xl text-xs leading-snug text-foreground/85 sm:text-sm">
              <span className="sm:hidden">Cookies: базовые; аналитика — с согласия.</span>
              <span className="hidden sm:inline">
                Обязательные cookies нужны для работы. Аналитика — только с вашего согласия.
              </span>{" "}
              <Link
                href="/legal/cookies"
                aria-label="Условия — Политика cookies"
                className="underline underline-offset-2 hover:text-foreground"
              >
                <span className="sm:hidden">Условия</span>
                <span className="hidden sm:inline">Политика cookies</span>
              </Link>
              .
            </p>
            <div
              role="group"
              aria-label="Выбор cookies"
              className="grid grid-cols-3 gap-1.5 sm:flex sm:flex-nowrap sm:items-center"
            >
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="min-h-11 px-1.5 text-xs sm:min-h-8 sm:px-3 sm:text-sm"
                aria-label="Настроить cookies"
                onClick={() => {
                  setIsCustomizing(true);
                }}
              >
                Настроить
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-11 px-1.5 text-xs sm:min-h-8 sm:px-3 sm:text-sm"
                aria-label="Принять только необходимые cookies"
                onClick={acceptNecessaryOnly}
              >
                Необходимые
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-11 px-1.5 text-xs sm:min-h-8 sm:px-3 sm:text-sm"
                aria-label="Принять всё"
                onClick={acceptAll}
              >
                Принять всё
              </Button>
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <h2 className="text-base font-semibold">Настройки cookies</h2>
            <div className="space-y-2">
              <CookieRow
                title="Необходимые"
                description="Авторизация, защита от CSRF, сохранение настроек. Без них Платформа не работает."
                disabled
                checked
              />
              <CookieRow
                title="Аналитические"
                description="Яндекс.Метрика — анализ поведения для улучшения Платформы."
                checked={analytics}
                onChange={setAnalytics}
              />
              <CookieRow
                title="Маркетинговые"
                description="Не используются в настоящий момент. Зарезервировано для будущего."
                checked={marketing}
                onChange={setMarketing}
              />
            </div>
            <div className="flex flex-wrap justify-end gap-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="min-h-11 min-w-11 sm:min-h-8"
                onClick={() => {
                  setIsCustomizing(false);
                }}
              >
                Назад
              </Button>
              <Button
                type="button"
                size="sm"
                className="min-h-11 min-w-11 sm:min-h-8"
                onClick={() => {
                  acceptCustom({ analytics, marketing });
                }}
              >
                Сохранить
              </Button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

interface CookieRowProps {
  title: string;
  description: string;
  checked: boolean;
  onChange?: (v: boolean) => void;
  disabled?: boolean;
}

function CookieRow({ title, description, checked, onChange, disabled }: CookieRowProps) {
  const labelId = `cookie-row-${title.replace(/\s+/g, "-").toLowerCase()}`;
  const descriptionId = `${labelId}-description`;
  return (
    <div className="flex items-start justify-between gap-3 rounded-lg border border-border/40 bg-background/40 p-2.5 sm:gap-4 sm:p-3">
      <div className="flex-1">
        <div id={labelId} className="text-sm font-medium">
          {title}
        </div>
        <div id={descriptionId} className="mt-0.5 text-xs text-muted-foreground">
          {description}
        </div>
      </div>
      <span className="relative flex min-h-11 min-w-11 shrink-0 items-center justify-end">
        <Switch
          className="after:absolute after:inset-0 after:content-['']"
          checked={checked}
          onCheckedChange={onChange}
          disabled={disabled}
          aria-labelledby={labelId}
          aria-describedby={descriptionId}
        />
      </span>
    </div>
  );
}
