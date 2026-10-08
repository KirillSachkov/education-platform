"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { useState } from "react";
import { useTrialCreditOverride } from "../model/use-trial-credit-override";

type TrialCreditOverrideDialogProps = {
  userId: string;
  planId: string;
  planName: string;
};

/**
 * Admin-диалог legacy override для «зачёта месячного доступа» (#580/#604).
 * Paid trial credit больше не сгорает; диалог оставлен для совместимости операций.
 * `until` опционален — пусто => бэкенд ставит дефолт +30д. Radix Dialog даёт
 * focus-trap + Esc + inert фон.
 */
export function TrialCreditOverrideDialog({
  userId,
  planId,
  planName,
}: TrialCreditOverrideDialogProps) {
  const [open, setOpen] = useState(false);
  const [until, setUntil] = useState("");
  const override = useTrialCreditOverride(userId);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (override.isPending) return;
    // Пусто => null (бэкенд ставит дефолт +30д). Иначе local datetime → ISO/UTC.
    const untilIso = until ? new Date(until).toISOString() : null;
    override.mutate(
      { planId, until: untilIso },
      {
        onSuccess: () => {
          setOpen(false);
          setUntil("");
        },
      },
    );
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (override.isPending) return;
        setOpen(next);
        if (!next) setUntil("");
      }}
    >
      <Button
        variant="ghost"
        size="sm"
        className="text-primary hover:bg-primary/10 hover:text-primary"
        onClick={() => setOpen(true)}
      >
        <Icons.clock className="size-4" />
        Отметить зачёт
      </Button>

      <DialogContent>
        <form onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Отметить зачёт месячного доступа</DialogTitle>
            <DialogDescription>
              Для плана «{planName}» будет сохранён ручной override. Сейчас оплаченный
              месяц уже идёт в зачёт без срока сгорания; эта отметка нужна только для
              совместимости старых операций.
            </DialogDescription>
          </DialogHeader>

          <div className="mt-4 space-y-2">
            <Label htmlFor="trial-credit-until">Override действует до (необязательно)</Label>
            <Input
              id="trial-credit-until"
              type="datetime-local"
              value={until}
              onChange={(e) => setUntil(e.target.value)}
              aria-describedby="trial-credit-until-hint"
            />
            <p id="trial-credit-until-hint" className="text-xs text-muted-foreground">
              Оставьте пустым — backend поставит 30 дней по умолчанию.
            </p>
          </div>

          <DialogFooter className="mt-6">
            <DialogClose asChild>
              <Button type="button" variant="outline" disabled={override.isPending}>
                Отмена
              </Button>
            </DialogClose>
            <Button type="submit" disabled={override.isPending}>
              {override.isPending ? (
                <>
                  <Icons.loading className="size-4 animate-spin" />
                  Сохраняем…
                </>
              ) : (
                "Отметить зачёт"
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
