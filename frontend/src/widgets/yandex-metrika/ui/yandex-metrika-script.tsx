"use client";

import Script from "next/script";
import { useEffect, useRef, useState } from "react";
import { useCookieConsent } from "@/shared/lib/use-cookie-consent";
import { YANDEX_METRIKA_READY_EVENT } from "@/shared/analytics";

const RAW_YANDEX_METRIKA_ID = process.env.NEXT_PUBLIC_YANDEX_METRIKA_ID;
const YANDEX_METRIKA_ID =
  RAW_YANDEX_METRIKA_ID && /^\d+$/.test(RAW_YANDEX_METRIKA_ID)
    ? Number(RAW_YANDEX_METRIKA_ID)
    : null;
const YANDEX_METRIKA_OPTIONS = {
  clickmap: true,
  trackLinks: true,
  accurateTrackBounce: true,
  webvisor: true,
} as const;

/**
 * Загружает скрипт Яндекс.Метрики только при наличии согласия пользователя
 * на категорию "analytics" cookies (через cookie-banner).
 *
 * Без NEXT_PUBLIC_YANDEX_METRIKA_ID — ничего не делает (dev / staging).
 * Без согласия — ничего не делает (юзер отказался от аналитики).
 */
export function YandexMetrikaScript() {
  const { consent } = useCookieConsent();
  const [hasLoaded, setHasLoaded] = useState(false);
  const isInitialized = useRef(false);

  useEffect(() => {
    const handleReady = () => {
      isInitialized.current = true;
      setHasLoaded(true);
    };

    window.addEventListener(YANDEX_METRIKA_READY_EVENT, handleReady);
    if (window.__sachkovMetrikaInitialized === true) handleReady();
    return () => {
      window.removeEventListener(YANDEX_METRIKA_READY_EVENT, handleReady);
    };
  }, []);

  useEffect(() => {
    if (YANDEX_METRIKA_ID === null || typeof window.ym !== "function") return;

    if (!consent?.analytics) {
      if (!isInitialized.current) return;
      try {
        window.ym(YANDEX_METRIKA_ID, "destruct");
      } finally {
        isInitialized.current = false;
        window.__sachkovMetrikaInitialized = false;
      }
      return;
    }

    if (!hasLoaded || isInitialized.current) return;
    window.ym(YANDEX_METRIKA_ID, "init", YANDEX_METRIKA_OPTIONS);
    isInitialized.current = true;
    window.__sachkovMetrikaInitialized = true;
    window.dispatchEvent(new Event(YANDEX_METRIKA_READY_EVENT));
  }, [consent?.analytics, hasLoaded]);

  if (YANDEX_METRIKA_ID === null) return null;
  if (!consent?.analytics) return null;
  if (hasLoaded) return null;

  return (
    <Script id="yandex-metrika" strategy="afterInteractive">
      {`
        (function(m,e,t,r,i,k,a){
          m[i]=m[i]||function(){(m[i].a=m[i].a||[]).push(arguments)};
          m[i].l=1*new Date();
          for (var j = 0; j < document.scripts.length; j++) {
            if (document.scripts[j].src === r) { return; }
          }
          k=e.createElement(t),a=e.getElementsByTagName(t)[0],k.async=1,k.src=r,a.parentNode.insertBefore(k,a)
        })(window, document, "script", "https://mc.yandex.ru/metrika/tag.js", "ym");

        ym(${YANDEX_METRIKA_ID}, "init", {
          clickmap:true,
          trackLinks:true,
          accurateTrackBounce:true,
          webvisor:true
        });
        window.__sachkovMetrikaInitialized = true;
        window.dispatchEvent(new Event("${YANDEX_METRIKA_READY_EVENT}"));
      `}
    </Script>
  );
}
