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
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";
import { useAdminRevokeGrant } from "../model/use-admin-revoke-grant";

type RevokeGrantDialogProps = {
  userId: string;
  grantId: string;
  planName: string;
};

/**
 * Admin confirm-dialog для ручного отзыва плана у юзера (#414). Причина (reason)
 * обязательна — submit заблокирован пока поле пустое, текст уходит в audit-trail.
 * Radix Dialog даёт focus-trap + Esc + inert фон, ручной trap не нужен.
 */
export function RevokeGrantDialog({ userId, grantId, planName }: RevokeGrantDialogProps) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const revoke = useAdminRevokeGrant(userId);

  const trimmed = reason.trim();
  const canSubmit = trimmed.length > 0 && !revoke.isPending;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canSubmit) return;
    revoke.mutate(
      { grantId, reason: trimmed },
      {
        onSuccess: () => {
          setOpen(false);
          setReason("");
        },
      },
    );
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        // Не сбрасываем ввод во время запроса, чтобы случайный клик вне диалога
        // не потерял введённую причину.
        if (revoke.isPending) return;
        setOpen(next);
        if (!next) setReason("");
      }}
    >
      <Button
        variant="ghost"
        size="sm"
        className="text-destructive hover:bg-destructive/10 hover:text-destructive"
        onClick={() => setOpen(true)}
      >
        <Icons.shieldAlert className="size-4" />
        Отозвать
      </Button>

      <DialogContent>
        <form onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Отозвать план у пользователя</DialogTitle>
            <DialogDescription>
              План «{planName}» будет отозван. Доступ к материалам плана пропадёт, действие
              попадёт в audit-trail. Отменить отзыв нельзя.
            </DialogDescription>
          </DialogHeader>

          <div className="mt-4 space-y-2">
            <Label htmlFor="revoke-reason">
              Причина отзыва <span className="text-destructive">*</span>
            </Label>
            <Textarea
              id="revoke-reason"
              required
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Например: возврат средств, нарушение правил, ошибочная выдача"
              aria-describedby="revoke-reason-hint"
              autoFocus
            />
            <p id="revoke-reason-hint" className="text-xs text-muted-foreground">
              Обязательное поле — будет видно в журнале действий.
            </p>
          </div>

          <DialogFooter className="mt-6">
            <DialogClose asChild>
              <Button type="button" variant="outline" disabled={revoke.isPending}>
                Отмена
              </Button>
            </DialogClose>
            <Button type="submit" variant="destructive" disabled={!canSubmit}>
              {revoke.isPending ? (
                <>
                  <Icons.loading className="size-4 animate-spin" />
                  Отзываем…
                </>
              ) : (
                "Отозвать план"
              )}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
