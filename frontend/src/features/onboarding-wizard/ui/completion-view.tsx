"use client";

import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";

type Props = {
  planTitle: string;
  onFinish: () => void;
  isFinishing: boolean;
};

export function CompletionView({ planTitle, onFinish, isFinishing }: Props) {
  return (
    <div className="flex flex-col items-center gap-5 px-6 py-10 text-center sm:py-12">
      <span className="flex size-14 items-center justify-center rounded-full bg-green/15 text-green">
        <Icons.completed className="size-7" />
      </span>
      <div className="space-y-1.5">
        <h2 className="text-2xl font-semibold">Всё готово</h2>
        <p className="text-balance text-muted-foreground">
          Онбординг к плану «{planTitle}» пройден. Удачной учёбы!
        </p>
      </div>
      <Button size="lg" className="w-full sm:w-auto" disabled={isFinishing} onClick={onFinish}>
        {isFinishing && <Icons.loading className="size-4 animate-spin" />}
        Начать обучение
      </Button>
    </div>
  );
}
