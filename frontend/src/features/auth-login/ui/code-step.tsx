"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSlot,
} from "@/shared/ui/kit/input-otp";
import { ArrowLeft, KeyRound, Loader2 } from "lucide-react";
import { useState } from "react";

const OTP_LENGTH = 6;

type Props = {
  email: string;
  onSubmit: (code: string) => void;
  onResend: () => void;
  onBack: () => void;
  isPending: boolean;
  cooldown: number;
  isCooldownActive: boolean;
  isResending: boolean;
};

export function CodeStep({
  email,
  onSubmit,
  onResend,
  onBack,
  isPending,
  cooldown,
  isCooldownActive,
  isResending,
}: Props) {
  const [code, setCode] = useState("");

  const handleComplete = (value: string) => {
    onSubmit(value);
  };

  const handleConfirm = () => {
    if (code.length === OTP_LENGTH) {
      onSubmit(code);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-center gap-2 text-center">
        <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10">
          <KeyRound className="size-5 text-primary" />
        </div>
        <p className="text-sm font-medium">Введите код</p>
        <p className="text-xs text-muted-foreground">
          Отправлен на{" "}
          <span className="font-medium text-foreground">{email}</span>
        </p>
      </div>

      <div className="flex justify-center">
        <InputOTP
          maxLength={OTP_LENGTH}
          value={code}
          onChange={setCode}
          onComplete={handleComplete}
          disabled={isPending}
          autoComplete="one-time-code"
        >
          <InputOTPGroup>
            {Array.from({ length: OTP_LENGTH }, (_, i) => (
              <InputOTPSlot key={i} index={i} />
            ))}
          </InputOTPGroup>
        </InputOTP>
      </div>

      <Button
        size="lg"
        className="w-full"
        onClick={handleConfirm}
        disabled={isPending || code.length !== OTP_LENGTH}
      >
        {isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
        Подтвердить
      </Button>

      <div className="flex items-center justify-between">
        <button
          type="button"
          className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
          onClick={onBack}
        >
          <ArrowLeft className="size-3" />
          Назад
        </button>

        <button
          type="button"
          className="text-xs text-muted-foreground hover:text-foreground transition-colors disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
          disabled={isCooldownActive || isResending}
          onClick={onResend}
        >
          {isCooldownActive
            ? `Отправить повторно (${cooldown}с)`
            : "Отправить повторно"}
        </button>
      </div>
    </div>
  );
}
