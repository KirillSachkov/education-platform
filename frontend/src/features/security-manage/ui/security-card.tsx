"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/ui/kit/card";
import { Separator } from "@/shared/ui/kit/separator";
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
import { Loader2, LogOut, ShieldAlert } from "lucide-react";
import { useRevokeSessions } from "../model/use-revoke-sessions";

export function SecurityCard() {
  const { revokeSessions, isPending } = useRevokeSessions();

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ShieldAlert size={16} />
          Безопасность
        </CardTitle>
        <CardDescription>
          Управление активными сессиями
        </CardDescription>
      </CardHeader>
      <Separator />
      <CardContent className="pt-6">
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="destructive" disabled={isPending}>
              {isPending ? (
                <Loader2 className="size-4 animate-spin mr-2" />
              ) : (
                <LogOut className="size-4 mr-2" />
              )}
              Выйти из всех устройств
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Завершить все сессии?</AlertDialogTitle>
              <AlertDialogDescription>
                Все активные сессии будут завершены, включая текущую. Вам
                потребуется войти заново.
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
      </CardContent>
    </Card>
  );
}
