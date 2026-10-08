"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { notificationsApi } from "@/entities/notification";
import { getErrorMessage } from "@/shared/api";
import { arrayBufferToBase64Url, urlBase64ToUint8Array } from "../lib/vapid";

const VAPID_PUBLIC_KEY = process.env.NEXT_PUBLIC_VAPID_PUBLIC_KEY ?? "";
const SW_URL = "/sw.js";

export type PushPermission = NotificationPermission | "unsupported";

export interface PushSubscriptionApi {
  /** Поддержан ли web-push в этом браузере + сконфигурирован ли VAPID-ключ. */
  isSupported: boolean;
  /** Статус разрешения на уведомления (`default`/`granted`/`denied`/`unsupported`). */
  permission: PushPermission;
  /** Есть ли активная push-подписка у этого устройства. */
  isSubscribed: boolean;
  isPending: boolean;
  /** Запрашивает разрешение + подписывает устройство. Возвращает `true` при успехе. */
  subscribe: () => Promise<boolean>;
  /** Отписывает это устройство. */
  unsubscribe: () => Promise<void>;
}

function detectSupported(): boolean {
  if (typeof window === "undefined") return false;
  return (
    "serviceWorker" in navigator &&
    "PushManager" in window &&
    "Notification" in window &&
    VAPID_PUBLIC_KEY.length > 0
  );
}

function readPermission(): PushPermission {
  if (typeof window === "undefined" || !("Notification" in window)) return "unsupported";
  return Notification.permission;
}

/** Гарантирует наличие зарегистрированного service worker'а (в dev его мог не поставить RegisterServiceWorker). */
async function ensureRegistration(): Promise<ServiceWorkerRegistration> {
  let registration = await navigator.serviceWorker.getRegistration();
  registration ??= await navigator.serviceWorker.register(SW_URL, { scope: "/" });
  // Ждём активации — pushManager доступен только на активном SW.
  return navigator.serviceWorker.ready.then(() => registration);
}

export function usePushSubscription(): PushSubscriptionApi {
  const isSupported = detectSupported();
  const [permission, setPermission] = useState<PushPermission>(readPermission);
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [isPending, setIsPending] = useState(false);

  // Читаем текущее состояние подписки устройства один раз после монтирования.
  useEffect(() => {
    if (!isSupported) return;
    let cancelled = false;
    void (async () => {
      const registration = await navigator.serviceWorker.getRegistration();
      const existing = registration ? await registration.pushManager.getSubscription() : null;
      if (!cancelled) setIsSubscribed(existing !== null);
    })();
    return () => {
      cancelled = true;
    };
  }, [isSupported]);

  const subscribe = async (): Promise<boolean> => {
    if (!isSupported) {
      toast.error("Push-уведомления не поддерживаются в этом браузере");
      return false;
    }

    setIsPending(true);
    try {
      const result = await Notification.requestPermission();
      setPermission(result);
      if (result !== "granted") {
        if (result === "denied") {
          toast.error("Уведомления заблокированы. Разрешите их в настройках браузера.");
        }
        return false;
      }

      const registration = await ensureRegistration();
      const subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(VAPID_PUBLIC_KEY),
      });

      await notificationsApi.registerPushSubscription({
        endpoint: subscription.endpoint,
        p256dh: arrayBufferToBase64Url(subscription.getKey("p256dh")),
        auth: arrayBufferToBase64Url(subscription.getKey("auth")),
        userAgent: navigator.userAgent,
      });

      setIsSubscribed(true);
      toast.success("Push-уведомления включены");
      return true;
    } catch (error) {
      toast.error(getErrorMessage(error, "Не удалось включить push-уведомления"));
      return false;
    } finally {
      setIsPending(false);
    }
  };

  const unsubscribe = async (): Promise<void> => {
    if (!isSupported) return;
    setIsPending(true);
    try {
      const registration = await navigator.serviceWorker.getRegistration();
      const subscription = registration
        ? await registration.pushManager.getSubscription()
        : null;

      if (subscription) {
        await notificationsApi.removePushSubscription({ endpoint: subscription.endpoint });
        await subscription.unsubscribe();
      }
      setIsSubscribed(false);
      toast.success("Push-уведомления выключены на этом устройстве");
    } catch (error) {
      toast.error(getErrorMessage(error, "Не удалось выключить push-уведомления"));
    } finally {
      setIsPending(false);
    }
  };

  return { isSupported, permission, isSubscribed, isPending, subscribe, unsubscribe };
}
