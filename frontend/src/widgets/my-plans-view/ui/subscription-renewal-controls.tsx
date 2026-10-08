"use client";

import { accessPlanApi, myGrantsQueryKey, type PlanGrantDto } from "@/entities/access-plan";
import { getErrorMessage } from "@/shared/api";
import { Icons } from "@/shared/ui/icons";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";

interface SubscriptionRenewalControlsProps {
  grant: PlanGrantDto;
}

export function hasSubscriptionLifecycle(grant: PlanGrantDto): boolean {
  return Boolean(grant.nextChargeAt || grant.renewalGraceEndsAt || grant.autoRenewalCancelledAt);
}

export function SubscriptionRenewalControls({ grant }: SubscriptionRenewalControlsProps) {
  const queryClient = useQueryClient();
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [renderedAt, setRenderedAt] = useState(Date.now);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const timer = window.setInterval(() => {
      setRenderedAt(Date.now());
    }, 60_000);
    return () => {
      window.clearInterval(timer);
    };
  }, []);

  const cancelRenewal = useMutation({
    mutationFn: () => accessPlanApi.cancelAutoRenewal(grant.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: myGrantsQueryKey });
      setCancelDialogOpen(false);
      toast.success("Автопродление отключено");
    },
    onError: (error) => {
      toast.error(getErrorMessage(error, "Не удалось отключить автопродление"));
    },
  });

  const resumeRenewal = useMutation({
    mutationFn: () => accessPlanApi.resumeAutoRenewal(grant.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: myGrantsQueryKey });
      toast.success(isTerminalDunning ? "Повтор оплаты запущен" : "Автопродление включено");
    },
    onError: (error) => {
      toast.error(
        getErrorMessage(
          error,
          isTerminalDunning ? "Не удалось повторить оплату" : "Не удалось включить автопродление",
        ),
      );
    },
  });

  const isCancelled = grant.autoRenewalCancelledAt !== null;
  const isTerminalDunning =
    !isCancelled &&
    grant.chargeFailureCount >= 3 &&
    grant.renewalGraceEndsAt !== null &&
    grant.nextChargeAt === null;
  const isRetrying =
    !isCancelled && grant.renewalGraceEndsAt !== null && grant.nextChargeAt !== null;
  const paidAccessEndsAt = grant.expiresAt;
  const canResume =
    isCancelled &&
    grant.status === "ACTIVE" &&
    paidAccessEndsAt !== null &&
    Date.parse(paidAccessEndsAt) > renderedAt;
  const canRetryPayment =
    isTerminalDunning &&
    grant.status === "ACTIVE" &&
    grant.renewalGraceEndsAt !== null &&
    Date.parse(grant.renewalGraceEndsAt) > renderedAt;

  const cancelDescription =
    paidAccessEndsAt !== null && Date.parse(paidAccessEndsAt) > renderedAt
      ? `Новых списаний не будет. Оплаченный доступ останется до ${formatDate(paidAccessEndsAt)}.`
      : "Новых списаний не будет. Льготный доступ завершится сразу.";

  return (
    <section className="mt-4 rounded-xl border border-border/50 bg-muted/20 p-3 sm:p-4">
      <div className="flex flex-wrap items-center gap-2">
        <RenewalStatusBadge
          isCancelled={isCancelled}
          isRetrying={isRetrying}
          isTerminalDunning={isTerminalDunning}
        />
      </div>

      <p className="mt-2 text-sm leading-relaxed text-muted-foreground" aria-live="polite">
        {isCancelled
          ? cancelledDescription(grant)
          : isTerminalDunning
            ? terminalDunningDescription(grant)
            : isRetrying
              ? "Мы попробуем списать оплату ещё раз. До конца льготного периода доступ сохранится."
              : "Подписка продлится автоматически в дату следующего списания."}
      </p>

      <dl className="mt-3 grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
        {grant.expiresAt ? (
          <LifecycleRow label="Оплачено до" value={formatDate(grant.expiresAt)} />
        ) : null}
        {grant.nextChargeAt ? (
          <LifecycleRow
            label={isRetrying ? "Следующая попытка" : "Следующее списание"}
            value={formatDateTime(grant.nextChargeAt)}
          />
        ) : null}
        {grant.renewalGraceEndsAt ? (
          <LifecycleRow
            label="Льготный доступ до"
            value={formatDateTime(grant.renewalGraceEndsAt)}
          />
        ) : null}
        {grant.chargeFailureCount > 0 ? (
          <LifecycleRow label="Неудачных попыток" value={String(grant.chargeFailureCount)} />
        ) : null}
        {isCancelled && grant.accessEndsAt ? (
          <LifecycleRow label="Доступ до" value={formatDate(grant.accessEndsAt)} />
        ) : null}
      </dl>

      <div className="mt-4 flex flex-col gap-2 sm:flex-row">
        {isTerminalDunning ? (
          canRetryPayment ? (
            <Button
              type="button"
              size="sm"
              className="min-h-11 w-full sm:w-auto"
              disabled={resumeRenewal.isPending}
              onClick={() => {
                resumeRenewal.mutate();
              }}
            >
              <Icons.refresh
                className={resumeRenewal.isPending ? "size-4 animate-spin" : "size-4"}
              />
              {resumeRenewal.isPending ? "Запускаем…" : "Повторить оплату"}
            </Button>
          ) : null
        ) : isCancelled ? (
          canResume ? (
            <Button
              type="button"
              size="sm"
              className="min-h-11 w-full sm:w-auto"
              disabled={resumeRenewal.isPending}
              onClick={() => {
                resumeRenewal.mutate();
              }}
            >
              <Icons.refresh
                className={resumeRenewal.isPending ? "size-4 animate-spin" : "size-4"}
              />
              {resumeRenewal.isPending ? "Включаем…" : "Возобновить автопродление"}
            </Button>
          ) : null
        ) : (
          <AlertDialog
            open={cancelDialogOpen}
            onOpenChange={(open) => {
              if (!cancelRenewal.isPending) setCancelDialogOpen(open);
            }}
          >
            <AlertDialogTrigger asChild>
              <Button
                type="button"
                size="sm"
                variant="outline"
                className="min-h-11 w-full text-destructive hover:text-destructive sm:w-auto"
              >
                Отключить автопродление
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent
              onOpenAutoFocus={(event) => {
                event.preventDefault();
                cancelButtonRef.current?.focus();
              }}
            >
              <AlertDialogHeader>
                <AlertDialogTitle>Отключить автопродление?</AlertDialogTitle>
                <AlertDialogDescription>{cancelDescription}</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel ref={cancelButtonRef} disabled={cancelRenewal.isPending}>
                  Оставить включённым
                </AlertDialogCancel>
                <AlertDialogAction
                  disabled={cancelRenewal.isPending}
                  className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                  onClick={(event) => {
                    event.preventDefault();
                    cancelRenewal.mutate();
                  }}
                >
                  {cancelRenewal.isPending ? (
                    <Icons.loading className="size-4 animate-spin" />
                  ) : null}
                  {cancelRenewal.isPending ? "Отключаем…" : "Отключить"}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        )}
      </div>
    </section>
  );
}

function RenewalStatusBadge({
  isCancelled,
  isRetrying,
  isTerminalDunning,
}: {
  isCancelled: boolean;
  isRetrying: boolean;
  isTerminalDunning: boolean;
}) {
  if (isCancelled) {
    return (
      <Badge variant="outline" className="gap-1.5 font-normal text-muted-foreground">
        <Icons.close className="size-3.5" />
        Автопродление отключено
      </Badge>
    );
  }

  if (isTerminalDunning) {
    return (
      <Badge
        variant="outline"
        className="gap-1.5 border-destructive/40 font-normal text-destructive"
      >
        <Icons.error className="size-3.5" />
        Автопродление остановлено
      </Badge>
    );
  }

  if (isRetrying) {
    return (
      <Badge
        variant="outline"
        className="gap-1.5 border-amber-500/40 font-normal text-amber-700 dark:text-amber-300"
      >
        <Icons.warning className="size-3.5" />
        Платёж не прошёл
      </Badge>
    );
  }

  return (
    <Badge
      variant="outline"
      className="gap-1.5 border-emerald-500/40 font-normal text-emerald-700 dark:text-emerald-300"
    >
      <Icons.creditCard className="size-3.5" />
      Автопродление включено
    </Badge>
  );
}

function LifecycleRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 sm:block">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm text-foreground/90">{value}</dd>
    </div>
  );
}

function cancelledDescription(grant: PlanGrantDto): string {
  const accessEndsAt = grant.accessEndsAt ?? grant.expiresAt;
  return accessEndsAt
    ? `Новых списаний не будет. Доступ действует до ${formatDate(accessEndsAt)}.`
    : "Новых списаний не будет.";
}

function terminalDunningDescription(grant: PlanGrantDto): string {
  return grant.renewalGraceEndsAt
    ? `Все попытки списания завершились ошибкой. Доступ сохранится до ${formatDate(grant.renewalGraceEndsAt)}.`
    : "Все попытки списания завершились ошибкой.";
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("ru-RU", {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("ru-RU", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}
