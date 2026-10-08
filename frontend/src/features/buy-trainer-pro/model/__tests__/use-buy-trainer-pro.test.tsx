import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useBuyTrainerPro } from "../use-buy-trainer-pro";

const createOrder = vi.fn();

vi.mock("@/entities/trainer-pro", () => ({
  trainerProApi: { createOrder: (...args: unknown[]) => createOrder(...args) },
}));

vi.mock("@/shared/api", () => ({
  getErrorMessage: (_error: unknown, fallback: string) => fallback,
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

const realLocation = window.location;

describe("useBuyTrainerPro", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    // Подменяем location на простой объект, чтобы перехватить href-редирект.
    delete (window as { location?: Location }).location;
    (window as unknown as { location: { href: string; pathname: string; search: string } }).location =
      { href: "", pathname: "/trainer/pro", search: "" };
  });

  afterEach(() => {
    (window as unknown as { location: Location }).location = realLocation;
  });

  it("POST'ит trainer-pro order с planId + idempotency-key и редиректит на paymentUrl", async () => {
    createOrder.mockResolvedValue({
      result: { orderId: "ord-1", paymentUrl: "https://pay.example/abc" },
    });

    const { result } = renderHook(() => useBuyTrainerPro(), { wrapper });

    act(() => result.current.buy("plan-7"));

    await waitFor(() => expect(window.location.href).toBe("https://pay.example/abc"));

    expect(createOrder).toHaveBeenCalledTimes(1);
    const [req, idempotencyKey] = createOrder.mock.calls[0];
    expect(req).toEqual({ planId: "plan-7" });
    expect(typeof idempotencyKey).toBe("string");
    expect((idempotencyKey as string).length).toBeGreaterThan(0);
    // orderId сохранён как fallback для /payment/success.
    expect(sessionStorage.getItem("billing.lastOrderId")).toBe("ord-1");
  });

  it("не редиректит и не падает, если бэкенд не вернул paymentUrl", async () => {
    createOrder.mockResolvedValue({ result: null });

    const { result } = renderHook(() => useBuyTrainerPro(), { wrapper });

    act(() => result.current.buy("plan-7"));

    await waitFor(() => expect(createOrder).toHaveBeenCalledTimes(1));
    expect(window.location.href).toBe("");
    expect(sessionStorage.getItem("billing.lastOrderId")).toBeNull();
  });
});
