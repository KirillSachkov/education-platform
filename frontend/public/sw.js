// Minimal service worker — offline strategy A (#260).
// Goal: serve a cached /offline shell when navigation requests fail,
// and stale-while-revalidate for static assets so the shell renders styled.
// No content caching (entitlement-gated material bodies must hit the network).

const VERSION = "v1";
const SHELL_CACHE = `pwa-shell-${VERSION}`;
const ASSET_CACHE = `pwa-assets-${VERSION}`;
const ASSET_CACHE_MAX_ENTRIES = 200;
const OFFLINE_URL = "/offline";

async function trimCache(cache, maxEntries) {
  const keys = await cache.keys();
  const overflow = keys.length - maxEntries;
  if (overflow <= 0) return;

  await Promise.all(keys.slice(0, overflow).map((key) => cache.delete(key)));
}

function resolveSameOriginPath(rawUrl, origin) {
  try {
    const target = new URL(rawUrl, origin);
    if (target.origin !== origin) return "/";
    return `${target.pathname}${target.search}${target.hash}`;
  } catch {
    return "/";
  }
}

self.addEventListener("install", (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(SHELL_CACHE);
      try {
        await cache.add(new Request(OFFLINE_URL, { cache: "reload" }));
      } catch (err) {
        console.warn("[sw] failed to precache offline page", err);
      }
      self.skipWaiting();
    })(),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    (async () => {
      const expected = new Set([SHELL_CACHE, ASSET_CACHE]);
      const names = await caches.keys();
      await Promise.all(names.filter((n) => !expected.has(n)).map((n) => caches.delete(n)));
      await self.clients.claim();
    })(),
  );
});

const ASSET_EXT = /\.(?:css|js|mjs|woff2?|ttf|otf|png|svg|webp|jpg|jpeg|gif|ico)$/i;

self.addEventListener("fetch", (event) => {
  const { request } = event;
  if (request.method !== "GET") return;

  const url = new URL(request.url);

  // Never touch the API — entitlement-gated, must be live.
  if (url.pathname.startsWith("/api/")) return;
  if (url.pathname.startsWith("/connect/")) return;
  if (url.pathname.startsWith("/auth/")) return;
  if (url.pathname.startsWith("/.well-known/")) return;
  // MinIO proxy. Files here are signed-URL or entitlement-gated (avatars,
  // course covers, paid material previews). Caching them in SWR would serve
  // stale bytes after the user's grant is revoked. Always go to network.
  if (url.pathname.startsWith("/storage/")) return;

  // Navigation requests: network-first → offline fallback.
  if (request.mode === "navigate") {
    event.respondWith(
      (async () => {
        try {
          return await fetch(request);
        } catch {
          const cache = await caches.open(SHELL_CACHE);
          const fallback = await cache.match(OFFLINE_URL);
          return fallback ?? Response.error();
        }
      })(),
    );
    return;
  }

  // Same-origin static assets: stale-while-revalidate.
  if (url.origin === self.location.origin && ASSET_EXT.test(url.pathname)) {
    const cachePromise = caches.open(ASSET_CACHE);
    const networkPromise = fetch(request);
    const updatePromise = Promise.all([cachePromise, networkPromise]).then(
      async ([cache, response]) => {
        if (response.ok && response.type === "basic") {
          await cache.put(request, response.clone());
          await trimCache(cache, ASSET_CACHE_MAX_ENTRIES);
        }
        return response;
      },
    );

    // Keep the background revalidation alive even when a cached response is
    // returned immediately. Cache failures must never fail the asset request.
    event.waitUntil(updatePromise.catch(() => undefined));
    event.respondWith(
      (async () => {
        const cache = await cachePromise;
        const cached = await cache.match(request);
        if (cached) return cached;

        try {
          return await updatePromise;
        } catch {
          return Response.error();
        }
      })(),
    );
  }
});

// --- Web Push (#342) ---------------------------------------------------------
// Payload shape (built by NotificationService WebPushNotificationChannel):
//   { title, body, url, tag }
const PUSH_ICON = "/icons/icon-192.png";
const PUSH_BADGE = "/icons/icon-192.png";

self.addEventListener("push", (event) => {
  let payload = {};
  try {
    payload = event.data ? event.data.json() : {};
  } catch {
    // Non-JSON payload — fall back to plain text body.
    payload = { body: event.data ? event.data.text() : "" };
  }

  const title = payload.title || "SachkovLearn";
  const options = {
    body: payload.body || "",
    icon: PUSH_ICON,
    badge: PUSH_BADGE,
    // tag = notification id → браузер схлопывает дубликаты одного уведомления.
    tag: payload.tag || undefined,
    // url прокидываем в notificationclick через data.
    data: { url: payload.url || "/" },
  };

  event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const origin = self.location.origin;
  // url задаётся нашим backend'ом (всегда same-origin), но на всякий случай не доверяем —
  // открываем только same-origin цель, иначе fallback на корень (защита от open-redirect,
  // если бы payload когда-нибудь стал недоверенным).
  const rawUrl = (event.notification.data && event.notification.data.url) || "/";
  const targetUrl = resolveSameOriginPath(rawUrl, origin);

  event.waitUntil(
    (async () => {
      const allClients = await self.clients.matchAll({
        type: "window",
        includeUncontrolled: true,
      });

      // Фокусируем уже открытую вкладку платформы (same-origin) и навигируем её.
      for (const client of allClients) {
        if (client.url.startsWith(origin) && "focus" in client) {
          await client.focus();
          if ("navigate" in client) {
            try {
              await client.navigate(targetUrl);
            } catch {
              // navigation отклонена — игнорируем, окно уже в фокусе.
            }
          }
          return;
        }
      }

      // Иначе открываем новое окно на target URL.
      if (self.clients.openWindow) {
        await self.clients.openWindow(targetUrl);
      }
    })(),
  );
});
