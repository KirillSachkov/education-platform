import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { renderHook } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// --- Hoisted fixtures ---
// Тесты должны verify-ить:
// 1) Хук открывает нативный EventSource с access_token в URL.
// 2) Не открывает при unauthenticated / пустом токене.
// 3) Invalidate query keys при notification.created.
// 4) Закрывает соединение при unmount.
// 5) Пересоздаёт соединение при ротации токена.
// 6) Открывает соединение СРАЗУ, не дожидаясь window.load (#457 — иначе ломается
//    native pull-to-refresh на мобильных).
//
// Транспорт — нативный браузерный `EventSource` (глобал), не polyfill, поэтому
// мокаем глобал через vi.stubGlobal, а не модуль.
const fixtures = vi.hoisted(() => {
  class FakeEventSource {
    static lastInstance: FakeEventSource | null = null;
    static allInstances: FakeEventSource[] = [];

    url: string;
    closed = false;
    onerror: (() => void) | null = null;
    private listeners = new Map<string, Set<() => void>>();

    constructor(url: string) {
      this.url = url;
      FakeEventSource.lastInstance = this;
      FakeEventSource.allInstances.push(this);
    }

    addEventListener(event: string, cb: () => void) {
      let set = this.listeners.get(event);
      if (!set) {
        set = new Set();
        this.listeners.set(event, set);
      }
      set.add(cb);
    }

    removeEventListener(event: string, cb: () => void) {
      this.listeners.get(event)?.delete(cb);
    }

    close() {
      this.closed = true;
    }

    /** Test helper — имитирует приход SSE-события. */
    emit(event: string) {
      this.listeners.get(event)?.forEach((cb) => cb());
    }

    /** Нативный EventSource не шлёт headers → токен в query-param `access_token`. */
    get accessToken(): string | null {
      const query = this.url.split("?")[1] ?? "";
      return new URLSearchParams(query).get("access_token");
    }
  }

  const tokenState = {
    accessToken: "token-A",
    status: "authenticated" as "authenticated" | "unauthenticated",
  };

  return { FakeEventSource, tokenState };
});

vi.mock("@/shared/auth/token-store", () => ({
  tokenStore: {
    getState: () => fixtures.tokenState,
    subscribe: (cb: () => void) => () => cb(),
  },
}));

vi.mock("zustand", () => ({
  useStore: (_store: unknown, selector: (s: typeof fixtures.tokenState) => unknown) =>
    selector(fixtures.tokenState),
}));

// --- Import hook AFTER mocks are registered ---
import { useNotificationStream } from "../use-notification-stream";

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

describe("useNotificationStream", () => {
  beforeEach(() => {
    fixtures.FakeEventSource.lastInstance = null;
    fixtures.FakeEventSource.allInstances = [];
    fixtures.tokenState.accessToken = "token-A";
    fixtures.tokenState.status = "authenticated";
    // Хук использует глобальный нативный EventSource — подменяем на фейк.
    vi.stubGlobal("EventSource", fixtures.FakeEventSource);
  });

  afterEach(() => {
    // Defensive: сбрасываем static state мока явно после теста (beforeEach тоже
    // сбрасывает, но hard fail в середине теста оставил бы leak до следующего beforeEach).
    fixtures.FakeEventSource.lastInstance = null;
    fixtures.FakeEventSource.allInstances = [];
    vi.unstubAllGlobals();
    vi.clearAllMocks();
  });

  it("открывает нативный EventSource с access_token в URL при authenticated", () => {
    renderHook(() => useNotificationStream(), { wrapper });

    expect(fixtures.FakeEventSource.lastInstance).not.toBeNull();
    const source = fixtures.FakeEventSource.lastInstance!;
    expect(source.url).toContain("/notifications/stream");
    expect(source.accessToken).toBe("token-A");
  });

  it("открывает соединение сразу, не дожидаясь window.load — регрессия pull-to-refresh (#457)", () => {
    // #455 откладывал открытие SSE до window.load (XHR-polyfill держал мобильный
    // loader) — это сломало native pull-to-refresh. Нативный EventSource не влияет
    // на page-load state, поэтому открываем сразу в effect'е, даже пока документ
    // ещё «loading». Тест фиксирует отсутствие window.load-гейта.
    const readyStateSpy = vi
      .spyOn(document, "readyState", "get")
      .mockReturnValue("loading");

    try {
      renderHook(() => useNotificationStream(), { wrapper });

      // Соединение открыто немедленно — без ожидания события window.load.
      expect(fixtures.FakeEventSource.lastInstance).not.toBeNull();
      expect(fixtures.FakeEventSource.lastInstance!.accessToken).toBe("token-A");
    } finally {
      readyStateSpy.mockRestore();
    }
  });

  it("не открывает соединение если unauthenticated", () => {
    fixtures.tokenState.accessToken = "";
    fixtures.tokenState.status = "unauthenticated";
    renderHook(() => useNotificationStream(), { wrapper });
    expect(fixtures.FakeEventSource.lastInstance).toBeNull();
  });

  it("не открывает соединение если accessToken пуст", () => {
    fixtures.tokenState.accessToken = "";
    fixtures.tokenState.status = "authenticated";
    renderHook(() => useNotificationStream(), { wrapper });
    expect(fixtures.FakeEventSource.lastInstance).toBeNull();
  });

  it("invalidate-ит list при notification.created + fetch'ит unread-count", () => {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    const invalidateSpy = vi.spyOn(client, "invalidateQueries");
    const fetchSpy = vi.spyOn(client, "fetchQuery");

    renderHook(() => useNotificationStream(), {
      wrapper: ({ children }) => (
        <QueryClientProvider client={client}>{children}</QueryClientProvider>
      ),
    });

    const source = fixtures.FakeEventSource.lastInstance!;
    source.emit("notification.created");

    // List → invalidate (paginated infinite-query, broadcast snapshot нерационально).
    const invalidateCalls = invalidateSpy.mock.calls.map(
      (c) => (c[0] as { queryKey: readonly unknown[] }).queryKey,
    );
    expect(invalidateCalls.some((k) => k[0] === "notifications" && k[1] === "list")).toBe(
      true,
    );

    // Unread-count → fetchQuery → leader получает свежее значение, broadcast'ит
    // snapshot non-leader'ам через BroadcastChannel (#155 #3).
    const fetchCalls = fetchSpy.mock.calls.map(
      (c) => (c[0] as { queryKey: readonly unknown[] }).queryKey,
    );
    expect(
      fetchCalls.some((k) => k[0] === "notifications" && k[1] === "unread-count"),
    ).toBe(true);
  });

  it("закрывает соединение при unmount", () => {
    const { unmount } = renderHook(() => useNotificationStream(), { wrapper });
    const source = fixtures.FakeEventSource.lastInstance!;
    expect(source.closed).toBe(false);

    unmount();
    expect(source.closed).toBe(true);
  });

  it("пересоздаёт соединение при ротации токена", () => {
    const { rerender } = renderHook(() => useNotificationStream(), { wrapper });

    const first = fixtures.FakeEventSource.lastInstance!;
    expect(first.accessToken).toBe("token-A");

    // Имитируем ротацию.
    fixtures.tokenState.accessToken = "token-B";
    rerender();

    expect(fixtures.FakeEventSource.allInstances.length).toBe(2);
    expect(first.closed).toBe(true);
    const second = fixtures.FakeEventSource.lastInstance!;
    expect(second.accessToken).toBe("token-B");
  });
});
