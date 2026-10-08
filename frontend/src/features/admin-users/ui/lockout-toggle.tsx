"use client";

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
import { Loader2, Lock, Unlock } from "lucide-react";
import { useSetLockout } from "../model/use-set-lockout";

type Props = {
  userId: string;
  isLockedOut: boolean;
  /** Compact icon-only rendering for tight mobile action rows (aria-label + tooltip). */
  iconOnly?: boolean;
};

export function LockoutToggle({ userId, isLockedOut, iconOnly = false }: Props) {
  const { setLockout, isPending } = useSetLockout();
  const label = isLockedOut ? "Разблокировать" : "Заблокировать";
  const tone = isLockedOut ? "text-teal hover:text-teal" : "text-orange hover:text-orange";

  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>
        <Button
          variant="ghost"
          size={iconOnly ? "icon" : "sm"}
          aria-label={iconOnly ? label : undefined}
          title={iconOnly ? label : undefined}
          className={iconOnly ? `min-touch w-full shrink-0 ${tone}` : `text-xs ${tone}`}
        >
          {isLockedOut ? <Unlock size={iconOnly ? 16 : 13} /> : <Lock size={iconOnly ? 16 : 13} />}
          {!iconOnly && label}
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {isLockedOut
              ? "Разблокировать пользователя?"
              : "Заблокировать пользователя?"}
          </AlertDialogTitle>
          <AlertDialogDescription>
            {isLockedOut
              ? "Пользователь сможет снова входить в систему."
              : "Пользователь не сможет войти в систему до разблокировки."}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
          <AlertDialogAction
            onClick={() =>
              setLockout({
                userId,
                request: { isLocked: !isLockedOut },
              })
            }
            disabled={isPending}
          >
            {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
            {isLockedOut ? "Разблокировать" : "Заблокировать"}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
