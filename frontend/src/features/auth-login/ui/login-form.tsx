"use client";

import { Loader2 } from "lucide-react";
import { signIn } from "next-auth/react";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { routes } from "@/shared/config/routes";
import { resolveWorkspaceCallback } from "@/shared/lib/workspace-callback";
import { CONSENT_MISSING_ERROR_CODE, VerifyOtpError, type VerifyOtpRequest } from "../model/api";
import { useOtpCooldown } from "../model/use-otp-cooldown";
import { useSendOtp } from "../model/use-send-otp";
import { useVerifyOtp } from "../model/use-verify-otp";
import { CodeStep } from "./code-step";
import { ConsentsStep, type Consents } from "./consents-step";
import { EmailStep } from "./email-step";

type Step = "email" | "code" | "consents" | "authenticating";

export function LoginForm() {
  const searchParams = useSearchParams();
  // Defense-in-depth: NextAuth редиректит через конкатенацию с baseUrl, но
  // валидируем на входе — единые правила с /onboarding/connections (next-path).
  const callbackUrl = resolveWorkspaceCallback(searchParams.get("callbackUrl"));
  const [step, setStep] = useState<Step>("email");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");

  const { sendOtp, isPending: isSending } = useSendOtp();
  const { verifyOtp, isPending: isVerifying } = useVerifyOtp();
  const { cooldown, isActive: isCooldownActive, start: startCooldown } = useOtpCooldown();

  const handleSendOtp = async (inputEmail: string) => {
    setEmail(inputEmail);
    await sendOtp({ email: inputEmail });
    startCooldown();
    setStep("code");
  };

  const completeSignIn = async (redirectTo: string) => {
    setStep("authenticating");
    try {
      await signIn("auth-service", { redirectTo });
    } catch {
      // signIn redirects the browser — fetch may abort during navigation
    }
  };

  const tryVerify = async (request: VerifyOtpRequest): Promise<boolean> => {
    try {
      await verifyOtp(request);
      return true;
    } catch (error) {
      // Если бэкенд требует обязательных согласий — переключаемся на ConsentsStep.
      // OTP-код в этом случае backend НЕ потребил (mandatory consent check вынесен
      // ДО VerifyAndConsumeAsync), поэтому ретрай с consents использует тот же code.
      if (error instanceof VerifyOtpError && error.code === CONSENT_MISSING_ERROR_CODE) {
        setStep("consents");
        return false;
      }
      // Любые другие ошибки уже отображены через toast в useVerifyOtp.onError.
      return false;
    }
  };

  const handleVerifyOtp = async (otpCode: string) => {
    setCode(otpCode);
    const ok = await tryVerify({ email, code: otpCode });
    if (ok) {
      await completeSignIn(callbackUrl);
    }
  };

  const handleSubmitConsents = async (consents: Consents) => {
    const ok = await tryVerify({
      email,
      code,
      consentOfferAccepted: consents.offer,
      consentPersonalDataAccepted: consents.pd,
      consentMarketingAccepted: consents.marketing,
    });
    // Пройденный consents-шаг = новая регистрация (#696): ведём в онбординг
    // привязок GitHub/Telegram, исходная цель сохраняется в `next`.
    if (ok) {
      await completeSignIn(
        `${routes.onboardingConnections}?next=${encodeURIComponent(callbackUrl)}`,
      );
    }
  };

  const handleResend = async () => {
    await sendOtp({ email });
    startCooldown();
  };

  const handleBackToEmail = () => setStep("email");
  const handleBackToCode = () => setStep("code");

  if (step === "authenticating") {
    return (
      <div className="flex flex-col items-center gap-3 py-6">
        <Loader2 className="size-8 animate-spin text-primary" />
        <p className="text-sm text-muted-foreground">Выполняется вход...</p>
      </div>
    );
  }

  if (step === "consents") {
    return (
      <ConsentsStep
        email={email}
        onSubmit={handleSubmitConsents}
        onBack={handleBackToCode}
        isPending={isVerifying}
      />
    );
  }

  if (step === "code") {
    return (
      <CodeStep
        email={email}
        onSubmit={handleVerifyOtp}
        onResend={handleResend}
        onBack={handleBackToEmail}
        isPending={isVerifying}
        cooldown={cooldown}
        isCooldownActive={isCooldownActive}
        isResending={isSending}
      />
    );
  }

  return <EmailStep onSubmit={handleSendOtp} isPending={isSending} defaultEmail={email} />;
}
