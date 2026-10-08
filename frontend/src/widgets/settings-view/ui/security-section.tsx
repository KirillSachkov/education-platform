"use client";

import { useRevokeSessions } from "@/features/security-manage";
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
import { Button } from "@/shared/ui/kit/button";

/**
 * Раздел «Безопасность» — управление сессиями. Здесь же появится смена пароля.
 */
export function SecuritySection() {
  const { revokeSessions, isPending } = useRevokeSessions();

  return (
    <div className="space-y-6">
      <section className="rounded-2xl border border-border/50 bg-card/40 p-5 sm:p-6">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-red-dim text-red">
            <Icons.logout className="size-4" />
          </span>
          <div className="min-w-0 flex-1">
            <h3 className="text-sm font-semibold text-foreground">Активные сессии</h3>
            <p className="mt-0.5 text-xs text-muted-foreground/90 leading-relaxed">
              Завершить все сессии, включая текущую. Тебе придётся войти заново на всех устройствах,
              где открыта платформа.
            </p>
          </div>
        </div>

        <div className="mt-5">
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="outline" disabled={isPending}>
                {isPending ? <Icons.loading className="size-4 animate-spin mr-2" /> : null}
                Завершить все сессии
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Завершить все сессии?</AlertDialogTitle>
                <AlertDialogDescription>
                  Все активные сессии будут завершены, включая текущую. Тебе потребуется войти
                  заново.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Отмена</AlertDialogCancel>
                <AlertDialogAction onClick={() => revokeSessions()}>
                  Завершить все сессии
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
      </section>

      <section className="rounded-2xl border border-dashed border-border/50 bg-card/20 p-5 sm:p-6">
        <div className="flex items-start gap-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-muted/60 text-muted-foreground">
            <Icons.password className="size-4" />
          </span>
          <div className="min-w-0">
            <h3 className="text-sm font-semibold text-foreground">Пароль</h3>
            <p className="mt-0.5 text-xs text-muted-foreground/90 leading-relaxed">
              Смена пароля появится в одном из ближайших обновлений.
            </p>
          </div>
        </div>
      </section>
    </div>
  );
}
