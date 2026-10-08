import type { PlanGrantDto } from "@/entities/access-plan";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubscriptionRenewalControls } from "../subscription-renewal-controls";

const cancelAutoRenewal = vi.hoisted(() => vi.fn());
const resumeAutoRenewal = vi.hoisted(() => vi.fn());
const successToast = vi.hoisted(() => vi.fn());
const errorToast = vi.hoisted(() => vi.fn());

vi.mock("@/entities/access-plan", () => ({
  accessPlanApi: {
    cancelAutoRenewal,
    resumeAutoRenewal,
  },
  myGrantsQueryKey: ["access", "me", "grants"],
}));

vi.mock("sonner", () => ({
  toast: { success: successToast, error: errorToast },
}));

function createGrant(overrides: Partial<PlanGrantDto> = {}): PlanGrantDto {
  return {
    id: "grant-1",
    userId: "user-1",
    planId: "plan-1",
    source: "PURCHASE",
    sourceRef: "order-1",
    grantedAt: "2026-07-01T09:00:00Z",
    expiresAt: "2099-08-01T09:00:00Z",
    status: "ACTIVE",
    revokedAt: null,
    revokeReason: null,
    nextChargeAt: "2099-07-30T09:00:00Z",
    chargeFailureCount: 0,
    renewalGraceEndsAt: null,
    autoRenewalCancelledAt: null,
    accessEndsAt: "2099-08-01T09:00:00Z",
    ...overrides,
  };
}

function renderControls(grant: PlanGrantDto) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <SubscriptionRenewalControls grant={grant} />
    </QueryClientProvider>,
  );
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

describe("SubscriptionRenewalControls", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows an active renewal and keeps the least destructive dialog action focused", async () => {
    renderControls(createGrant());

    expect(screen.getByText("Автопродление включено")).toBeInTheDocument();
    expect(screen.getByText("Следующее списание")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Отключить автопродление" }));

    expect(await screen.findByRole("alertdialog")).toBeInTheDocument();
    const keepEnabled = screen.getByRole("button", { name: "Оставить включённым" });
    await waitFor(() => expect(keepEnabled).toHaveFocus());

    fireEvent.keyDown(screen.getByRole("alertdialog"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
  });

  it("shows failed charge, retry and grace details", () => {
    renderControls(
      createGrant({
        nextChargeAt: "2099-08-02T09:00:00Z",
        chargeFailureCount: 2,
        renewalGraceEndsAt: "2099-08-08T09:00:00Z",
        accessEndsAt: "2099-08-08T09:00:00Z",
      }),
    );

    expect(screen.getByText("Платёж не прошёл")).toBeInTheDocument();
    expect(screen.getByText("Следующая попытка")).toBeInTheDocument();
    expect(screen.getByText("Льготный доступ до")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
  });

  it("offers payment retry for terminal dunning without voluntary cancellation", async () => {
    resumeAutoRenewal.mockResolvedValue({ result: createGrant() });
    renderControls(
      createGrant({
        nextChargeAt: null,
        chargeFailureCount: 3,
        renewalGraceEndsAt: "2099-08-08T09:00:00Z",
        accessEndsAt: "2099-08-08T09:00:00Z",
      }),
    );

    expect(screen.getByText("Автопродление остановлено")).toBeInTheDocument();
    expect(screen.queryByText(/попробуем списать оплату ещё раз/i)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Отключить автопродление" }),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Повторить оплату" }));

    await waitFor(() => {
      expect(resumeAutoRenewal).toHaveBeenCalledWith("grant-1");
    });
    expect(successToast).toHaveBeenCalledWith("Повтор оплаты запущен");
  });

  it("does not offer payment retry after terminal grace ends", () => {
    renderControls(
      createGrant({
        expiresAt: "2020-01-01T00:00:00Z",
        nextChargeAt: null,
        chargeFailureCount: 3,
        renewalGraceEndsAt: "2020-01-08T00:00:00Z",
        accessEndsAt: "2020-01-08T00:00:00Z",
      }),
    );

    expect(screen.getByText("Автопродление остановлено")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Повторить оплату" })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Отключить автопродление" }),
    ).not.toBeInTheDocument();
  });

  it("keeps the cancel dialog pending until the mutation succeeds", async () => {
    const request = deferred<{ result: PlanGrantDto }>();
    cancelAutoRenewal.mockReturnValue(request.promise);
    const grant = createGrant();
    renderControls(grant);

    fireEvent.click(screen.getByRole("button", { name: "Отключить автопродление" }));
    fireEvent.click(await screen.findByRole("button", { name: "Отключить" }));

    await waitFor(() => {
      expect(cancelAutoRenewal).toHaveBeenCalledWith("grant-1");
    });
    expect(screen.getByRole("button", { name: "Отключаем…" })).toBeDisabled();
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();

    await act(async () => {
      request.resolve({
        result: createGrant({
          nextChargeAt: null,
          autoRenewalCancelledAt: "2026-07-12T10:00:00Z",
        }),
      });
      await request.promise;
    });

    await waitFor(() => {
      expect(successToast).toHaveBeenCalledWith("Автопродление отключено");
    });
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });

  it("allows resume only before paid access ends and announces an error", async () => {
    resumeAutoRenewal.mockRejectedValue(new Error("network"));
    const cancelledGrant = createGrant({
      nextChargeAt: null,
      autoRenewalCancelledAt: "2026-07-12T10:00:00Z",
    });
    const { rerender } = renderControls(cancelledGrant);

    expect(screen.getByText("Автопродление отключено")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Возобновить автопродление" }));

    await waitFor(() => {
      expect(errorToast).toHaveBeenCalledOnce();
    });

    rerender(
      <QueryClientProvider
        client={new QueryClient({ defaultOptions: { mutations: { retry: false } } })}
      >
        <SubscriptionRenewalControls
          grant={createGrant({
            expiresAt: "2020-01-01T00:00:00Z",
            accessEndsAt: "2020-01-01T00:00:00Z",
            nextChargeAt: null,
            autoRenewalCancelledAt: "2019-12-01T00:00:00Z",
          })}
        />
      </QueryClientProvider>,
    );

    expect(
      screen.queryByRole("button", { name: "Возобновить автопродление" }),
    ).not.toBeInTheDocument();
  });
});
