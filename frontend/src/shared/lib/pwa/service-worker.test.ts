import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it, vi } from "vitest";

type ServiceWorkerHandler = (event: Record<string, unknown>) => void;

function loadServiceWorker({
  cache,
  clients,
  fetch,
}: {
  cache?: Record<string, unknown>;
  clients?: Record<string, unknown>;
  fetch?: ReturnType<typeof vi.fn>;
} = {}) {
  const handlers = new Map<string, ServiceWorkerHandler>();
  const worker = {
    location: { origin: "https://platform.test" },
    clients: clients ?? {
      matchAll: vi.fn().mockResolvedValue([]),
      openWindow: vi.fn().mockResolvedValue(undefined),
    },
    registration: { showNotification: vi.fn().mockResolvedValue(undefined) },
    skipWaiting: vi.fn(),
    addEventListener: (type: string, handler: ServiceWorkerHandler) => {
      handlers.set(type, handler);
    },
  };
  const cacheStorage = {
    open: vi.fn().mockResolvedValue(
      cache ?? {
        add: vi.fn().mockResolvedValue(undefined),
        match: vi.fn().mockResolvedValue(undefined),
        put: vi.fn().mockResolvedValue(undefined),
        keys: vi.fn().mockResolvedValue([]),
        delete: vi.fn().mockResolvedValue(true),
      },
    ),
    keys: vi.fn().mockResolvedValue([]),
    delete: vi.fn().mockResolvedValue(true),
  };
  const source = readFileSync(resolve(process.cwd(), "public/sw.js"), "utf8");
  // The service worker is a standalone browser script, so this test executes it
  // inside a deliberately limited mock scope rather than importing app modules.
  // eslint-disable-next-line @typescript-eslint/no-implied-eval
  const evaluate = new Function(
    "self",
    "caches",
    "Request",
    "Response",
    "URL",
    "console",
    "fetch",
    source,
  ) as (...args: unknown[]) => void;

  evaluate(
    worker,
    cacheStorage,
    Request,
    Response,
    URL,
    console,
    fetch ?? vi.fn().mockRejectedValue(new Error("offline")),
  );

  return { handlers, worker, cacheStorage };
}

describe("service worker", () => {
  it.each([
    ["//evil.example/phish", "/"],
    ["https://platform.test.evil.example/phish", "/"],
    ["https://platform.test/courses/dotnet?from=push#lesson", "/courses/dotnet?from=push#lesson"],
  ])("keeps notification navigation on origin for %s", async (rawUrl, expected) => {
    const navigate = vi.fn().mockResolvedValue(undefined);
    const clients = {
      matchAll: vi
        .fn()
        .mockResolvedValue([{ url: "https://platform.test/home", focus: vi.fn(), navigate }]),
      openWindow: vi.fn().mockResolvedValue(undefined),
    };
    const { handlers } = loadServiceWorker({ clients });
    let pending: Promise<unknown> | undefined;

    handlers.get("notificationclick")?.({
      notification: {
        close: vi.fn(),
        data: { url: rawUrl },
      },
      waitUntil: (promise: Promise<unknown>) => {
        pending = promise;
      },
    });
    await pending;

    expect(navigate).toHaveBeenCalledWith(expected);
  });

  it("bounds the stale-while-revalidate asset cache", async () => {
    const oldKeys = Array.from({ length: 202 }, (_, index) => ({
      url: `/old-${String(index)}.js`,
    }));
    const cachedResponse = { source: "cache" };
    const networkResponse = {
      ok: true,
      type: "basic",
      clone: vi.fn().mockReturnValue({ source: "network-clone" }),
    };
    const cache = {
      match: vi.fn().mockResolvedValue(cachedResponse),
      put: vi.fn().mockResolvedValue(undefined),
      keys: vi.fn().mockResolvedValue(oldKeys),
      delete: vi.fn().mockResolvedValue(true),
    };
    const fetch = vi.fn().mockResolvedValue(networkResponse);
    const { handlers } = loadServiceWorker({ cache, fetch });
    let response: Promise<unknown> | undefined;
    let background: Promise<unknown> | undefined;

    handlers.get("fetch")?.({
      request: {
        method: "GET",
        mode: "no-cors",
        url: "https://platform.test/_next/static/chunks/app.js",
      },
      respondWith: (promise: Promise<unknown>) => {
        response = promise;
      },
      waitUntil: (promise: Promise<unknown>) => {
        background = promise;
      },
    });

    await expect(response).resolves.toBe(cachedResponse);
    await background;
    expect(cache.put).toHaveBeenCalledTimes(1);
    expect(cache.delete).toHaveBeenCalledTimes(2);
    expect(cache.delete).toHaveBeenNthCalledWith(1, oldKeys[0]);
    expect(cache.delete).toHaveBeenNthCalledWith(2, oldKeys[1]);
  });
});
