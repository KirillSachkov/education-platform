/**
 * VAPID / Web Push key helpers (#342).
 *
 * The browser `PushManager.subscribe` needs the server VAPID public key as a
 * `Uint8Array`; the server needs the device's `p256dh`/`auth` keys as base64url
 * strings. These two converters bridge the formats.
 */

/**
 * base64url VAPID public key → Uint8Array for `applicationServerKey`.
 * Returns `Uint8Array<ArrayBuffer>` (not `ArrayBufferLike`) so it satisfies the
 * `BufferSource` type `PushManager.subscribe` expects under strict lib.dom typings.
 */
export function urlBase64ToUint8Array(base64String: string): Uint8Array<ArrayBuffer> {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = atob(base64);
  const output = new Uint8Array(new ArrayBuffer(raw.length));
  for (let i = 0; i < raw.length; i++) {
    output[i] = raw.charCodeAt(i);
  }
  return output;
}

/** ArrayBuffer (from `PushSubscription.getKey`) → base64url string for the API. */
export function arrayBufferToBase64Url(buffer: ArrayBuffer | null): string {
  if (!buffer) return "";
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
