"use client";

import Link from "next/link";
import { useState } from "react";
import { Button } from "@/shared/ui/kit/button";
import { Checkbox } from "@/shared/ui/kit/checkbox";
import { Icons } from "@/shared/ui/icons";

export interface Consents {
  offer: boolean;
  pd: boolean;
  marketing: boolean;
}

type Props = {
  email: string;
  onSubmit: (consents: Consents) => void;
  onBack: () => void;
  isPending: boolean;
};

export function ConsentsStep({ email, onSubmit, onBack, isPending }: Props) {
  const [consents, setConsents] = useState<Consents>({
    offer: false,
    pd: false,
    marketing: false,
  });

  const allMandatoryAccepted = consents.offer && consents.pd;

  const handleSubmit = () => {
    if (allMandatoryAccepted && !isPending) {
      onSubmit(consents);
    }
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col items-center gap-2 text-center">
        <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10">
          <Icons.shieldCheck className="size-5 text-primary" />
        </div>
        <p className="text-sm font-medium">Завершение регистрации</p>
        <p className="text-xs text-muted-foreground">
          Создаём аккаунт для{" "}
          <span className="font-medium text-foreground">{email}</span>
        </p>
      </div>

      {/* Документы открываются в новой вкладке намеренно (исключение из правила
          «SPA = одна вкладка», #498): переход в той же вкладке терял бы
          заполняемую форму логина. */}
      <div className="space-y-2.5">
        <ConsentRow
          checked={consents.offer}
          onChange={(v) => setConsents({ ...consents, offer: v })}
          required
        >
          Я ознакомился(-ась) и согласен(-на) с{" "}
          <Link
            href="/legal/offer"
            target="_blank"
            className="underline hover:text-foreground transition-colors"
          >
            Договором-офертой
          </Link>
        </ConsentRow>

        <ConsentRow
          checked={consents.pd}
          onChange={(v) => setConsents({ ...consents, pd: v })}
          required
        >
          Я даю{" "}
          <Link
            href="/legal/consent-pd"
            target="_blank"
            className="underline hover:text-foreground transition-colors"
          >
            согласие на обработку моих персональных данных
          </Link>{" "}
          согласно{" "}
          <Link
            href="/legal/privacy"
            target="_blank"
            className="underline hover:text-foreground transition-colors"
          >
            Политике
          </Link>
        </ConsentRow>

        <ConsentRow
          checked={consents.marketing}
          onChange={(v) => setConsents({ ...consents, marketing: v })}
          required={false}
        >
          Согласен получать рекламные и информационные рассылки на email{" "}
          <span className="text-muted-foreground/70">(опционально)</span>
        </ConsentRow>
      </div>

      <Button
        size="lg"
        className="w-full"
        onClick={handleSubmit}
        disabled={!allMandatoryAccepted || isPending}
      >
        {isPending && <Icons.loading className="mr-2 size-4 animate-spin" />}
        Зарегистрироваться
      </Button>

      <button
        type="button"
        className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer self-start"
        onClick={onBack}
      >
        <Icons.back className="size-3" />
        Назад
      </button>
    </div>
  );
}

interface ConsentRowProps {
  checked: boolean;
  onChange: (v: boolean) => void;
  required: boolean;
  children: React.ReactNode;
}

function ConsentRow({ checked, onChange, required, children }: ConsentRowProps) {
  return (
    <label className="flex items-start gap-2 text-sm cursor-pointer">
      <Checkbox
        checked={checked}
        onCheckedChange={(v) => onChange(Boolean(v))}
        className="mt-0.5"
      />
      <span className="leading-snug text-foreground/90">
        {required && <span className="text-red mr-1" aria-label="обязательно">*</span>}
        {children}
      </span>
    </label>
  );
}
