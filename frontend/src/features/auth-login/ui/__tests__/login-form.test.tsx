import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CONSENT_MISSING_ERROR_CODE, VerifyOtpError } from "../../model/api";
import { PRICING_INTENT_STORAGE_KEY } from "@/shared/lib/pricing-intent";

const { trackGrowthEvent } = vi.hoisted(() => ({ trackGrowthEvent: vi.fn() }));

// Тестируется маршрутизация LoginForm (existing user → callbackUrl,
// новая регистрация через consents-шаг → /onboarding/connections?next=…),
// поэтому шаги-дети заменены минимальными заглушками, дёргающими колбэки.
vi.mock("next-auth/react", () => ({ signIn: vi.fn() }));
vi.mock("next/navigation", () => ({ useSearchParams: vi.fn() }));
vi.mock("../../model/use-send-otp", () => ({ useSendOtp: vi.fn() }));
vi.mock("../../model/use-verify-otp", () => ({ useVerifyOtp: vi.fn() }));
vi.mock("../../model/use-otp-cooldown", () => ({ useOtpCooldown: vi.fn() }));
vi.mock("@/shared/analytics", () => ({ trackGrowthEvent }));
vi.mock("../email-step", () => ({
  EmailStep: ({ onSubmit }: { onSubmit: (email: string) => void }) => (
    <button onClick={() => onSubmit("user@example.com")}>submit-email</button>
  ),
}));
vi.mock("../code-step", () => ({
  CodeStep: ({
    onSubmit,
    onResend,
    onBack,
  }: {
    onSubmit: (code: string) => void;
    onResend: () => void;
    onBack: () => void;
  }) => (
    <div>
      <button
        onClick={() => {
          onSubmit("123456");
        }}
      >
        submit-code
      </button>
      <button onClick={onResend}>resend-code</button>
      <button onClick={onBack}>back-to-email</button>
    </div>
  ),
}));
vi.mock("../consents-step", () => ({
  ConsentsStep: ({
    onSubmit,
  }: {
    onSubmit: (c: { offer: boolean; pd: boolean; marketing: boolean }) => void;
  }) => (
    <button onClick={() => onSubmit({ offer: true, pd: true, marketing: false })}>
      submit-consents
    </button>
  ),
}));

import { signIn } from "next-auth/react";
import { useSearchParams } from "next/navigation";
import { useOtpCooldown } from "../../model/use-otp-cooldown";
import { useSendOtp } from "../../model/use-send-otp";
import { useVerifyOtp } from "../../model/use-verify-otp";
import { LoginForm } from "../login-form";

const mockSignIn = vi.mocked(signIn);
const mockUseSearchParams = vi.mocked(useSearchParams);
const mockUseSendOtp = vi.mocked(useSendOtp);
const mockUseVerifyOtp = vi.mocked(useVerifyOtp);
const mockUseOtpCooldown = vi.mocked(useOtpCooldown);

function setup({
  callbackUrl,
  verifyOtp,
}: {
  callbackUrl?: string;
  verifyOtp: ReturnType<typeof vi.fn>;
}) {
  mockUseSearchParams.mockReturnValue(
    new URLSearchParams(callbackUrl ? { callbackUrl } : {}) as unknown as ReturnType<
      typeof useSearchParams
    >,
  );
  mockUseSendOtp.mockReturnValue({
    sendOtp: vi.fn().mockResolvedValue(undefined),
    isPending: false,
  } as unknown as ReturnType<typeof useSendOtp>);
  mockUseVerifyOtp.mockReturnValue({ verifyOtp, isPending: false } as unknown as ReturnType<
    typeof useVerifyOtp
  >);
  mockUseOtpCooldown.mockReturnValue({
    cooldown: 0,
    isActive: false,
    start: vi.fn(),
  } as unknown as ReturnType<typeof useOtpCooldown>);

  return render(<LoginForm />);
}

async function goThroughEmailAndCode() {
  fireEvent.click(screen.getByText("submit-email"));
  fireEvent.click(await screen.findByText("submit-code"));
}

const PRICING_INTENT_ID = "0190f4d8-8f6e-7a30-9d8f-4d76f8f86d61";
const PRICING_CALLBACK = `/pricing?plan=dotnet-fullstack&intent=${PRICING_INTENT_ID}&resume=checkout`;

function storePricingIntent() {
  window.sessionStorage.setItem(
    PRICING_INTENT_STORAGE_KEY,
    JSON.stringify({
      intentId: PRICING_INTENT_ID,
      planSlug: "dotnet-fullstack",
      createdAt: new Date().toISOString(),
    }),
  );
}

describe("LoginForm", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.sessionStorage.clear();
  });

  it("preserves a validated pricing callback and correlates completed auth", async () => {
    storePricingIntent();
    const verifyOtp = vi.fn().mockResolvedValue(undefined);
    setup({ callbackUrl: PRICING_CALLBACK, verifyOtp });

    await goThroughEmailAndCode();
    await screen.findByText("Выполняется вход...");

    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "auth_completed",
        properties: {
          flow: "checkout",
          new_account: false,
          correlation_id: PRICING_INTENT_ID,
        },
      },
      { once: `pricing-auth:${PRICING_INTENT_ID}` },
    );
    expect(mockSignIn).toHaveBeenCalledWith("auth-service", {
      redirectTo: PRICING_CALLBACK,
    });
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
  });

  it("preserves pricing intent through new-account consents and onboarding next", async () => {
    storePricingIntent();
    const verifyOtp = vi
      .fn()
      .mockRejectedValueOnce(new VerifyOtpError("consents required", CONSENT_MISSING_ERROR_CODE))
      .mockResolvedValueOnce(undefined);
    setup({ callbackUrl: PRICING_CALLBACK, verifyOtp });

    await goThroughEmailAndCode();
    fireEvent.click(await screen.findByText("submit-consents"));
    await screen.findByText("Выполняется вход...");

    expect(trackGrowthEvent).toHaveBeenCalledWith(
      {
        name: "auth_completed",
        properties: {
          flow: "checkout",
          new_account: true,
          correlation_id: PRICING_INTENT_ID,
        },
      },
      { once: `pricing-auth:${PRICING_INTENT_ID}` },
    );
    expect(mockSignIn).toHaveBeenCalledWith("auth-service", {
      redirectTo: `/onboarding/connections?next=${encodeURIComponent(PRICING_CALLBACK)}`,
    });
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
  });

  it("preserves pricing intent on resend, back and OTP error", async () => {
    storePricingIntent();
    const verifyOtp = vi
      .fn()
      .mockRejectedValue(new VerifyOtpError("wrong code", "auth.otp.invalid"));
    setup({ callbackUrl: PRICING_CALLBACK, verifyOtp });

    fireEvent.click(screen.getByText("submit-email"));
    fireEvent.click(await screen.findByText("resend-code"));
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();

    fireEvent.click(screen.getByText("submit-code"));
    expect(await screen.findByText("submit-code")).toBeInTheDocument();
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();

    fireEvent.click(screen.getByText("back-to-email"));
    expect(await screen.findByText("submit-email")).toBeInTheDocument();
    expect(window.sessionStorage.getItem(PRICING_INTENT_STORAGE_KEY)).not.toBeNull();
  });

  it("existing user: OTP verify signs in straight to callbackUrl (no onboarding)", async () => {
    const verifyOtp = vi.fn().mockResolvedValue(undefined);
    setup({ callbackUrl: "/courses/dotnet", verifyOtp });

    await goThroughEmailAndCode();
    await screen.findByText("Выполняется вход...");

    expect(verifyOtp).toHaveBeenCalledWith({ email: "user@example.com", code: "123456" });
    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockSignIn).toHaveBeenCalledWith("auth-service", {
      redirectTo: "/courses/dotnet",
    });
  });

  it("new user: consents step completion redirects to /onboarding/connections with next", async () => {
    const verifyOtp = vi
      .fn()
      .mockRejectedValueOnce(new VerifyOtpError("consents required", CONSENT_MISSING_ERROR_CODE))
      .mockResolvedValueOnce(undefined);
    setup({ callbackUrl: "/courses/dotnet", verifyOtp });

    await goThroughEmailAndCode();

    // Consents-шаг показан, вход ещё не выполнен
    const consentsButton = await screen.findByText("submit-consents");
    expect(mockSignIn).not.toHaveBeenCalled();

    fireEvent.click(consentsButton);
    await screen.findByText("Выполняется вход...");

    expect(verifyOtp).toHaveBeenLastCalledWith({
      email: "user@example.com",
      code: "123456",
      consentOfferAccepted: true,
      consentPersonalDataAccepted: true,
      consentMarketingAccepted: false,
    });
    expect(mockSignIn).toHaveBeenCalledTimes(1);
    expect(mockSignIn).toHaveBeenCalledWith("auth-service", {
      redirectTo: "/onboarding/connections?next=%2Fcourses%2Fdotnet",
    });
  });

  it("new user without callbackUrl: next falls back to /home", async () => {
    const verifyOtp = vi
      .fn()
      .mockRejectedValueOnce(new VerifyOtpError("consents required", CONSENT_MISSING_ERROR_CODE))
      .mockResolvedValueOnce(undefined);
    setup({ verifyOtp });

    await goThroughEmailAndCode();
    fireEvent.click(await screen.findByText("submit-consents"));
    await screen.findByText("Выполняется вход...");

    expect(mockSignIn).toHaveBeenCalledWith("auth-service", {
      redirectTo: "/onboarding/connections?next=%2Fhome",
    });
  });

  it("non-consent verify errors do not open the consents step or sign in", async () => {
    const verifyOtp = vi
      .fn()
      .mockRejectedValue(new VerifyOtpError("wrong code", "auth.otp.invalid"));
    setup({ verifyOtp });

    await goThroughEmailAndCode();

    // Остаёмся на code-шаге: ни consents, ни signIn
    expect(await screen.findByText("submit-code")).toBeInTheDocument();
    expect(screen.queryByText("submit-consents")).not.toBeInTheDocument();
    expect(mockSignIn).not.toHaveBeenCalled();
  });
});
