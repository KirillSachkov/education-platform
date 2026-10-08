"use client";

import { planTelegramChatQueryOptions } from "@/entities/plan-telegram-chat";
import type { ChatBindingDto } from "@/entities/plan-telegram-chat";
import { getErrorMessage } from "@/shared/api";
import { DeleteConfirmDialog } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Switch } from "@/shared/ui/kit/switch";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useUnbindChat } from "../model/use-unbind-chat";
import { useUpdateChatFlags } from "../model/use-update-chat-flags";
import { BindChatDialog } from "./bind-chat-dialog";

interface Props {
  planId: string;
}

export function ChatBindingsList({ planId }: Props) {
  const {
    data: bindings,
    isLoading,
    error,
  } = useQuery(planTelegramChatQueryOptions.list(planId));
  const unbind = useUnbindChat(planId);
  const updateFlags = useUpdateChatFlags(planId);

  const [bindOpen, setBindOpen] = useState(false);

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (error) {
    return (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Ошибка загрузки привязок чатов")}
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">Telegram-чаты плана</h2>
          <p className="text-sm text-muted-foreground">
            Чаты, доступом к которым управляет бот для участников плана. Invite-ссылки безопасно
            пересылать — бот сам пускает только пользователей с активным plan-grant.
          </p>
        </div>
        <Button type="button" onClick={() => setBindOpen(true)} size="sm">
          <Icons.add className="mr-2 h-4 w-4" />
          Привязать чат
        </Button>
      </div>

      {bindings && bindings.length === 0 ? (
        <EmptyState
          variant="dashed"
          icon={Icons.comment}
          title="Нет привязанных чатов"
          description="Добавьте бота админом в группу или канал и привяжите чат к этому плану."
        />
      ) : (
        <ul className="space-y-3">
          {bindings?.map((b) => (
            <BindingCard
              key={b.id}
              binding={b}
              isPending={unbind.isPending || updateFlags.isPending}
              onDelete={() => unbind.mutate(b.id)}
              onFlagChange={(flags) => updateFlags.mutate({ bindingId: b.id, request: flags })}
            />
          ))}
        </ul>
      )}

      <BindChatDialog planId={planId} open={bindOpen} onOpenChange={setBindOpen} />
    </div>
  );
}

interface CardProps {
  binding: ChatBindingDto;
  isPending: boolean;
  onDelete: () => void;
  onFlagChange: (flags: {
    enrollmentGrantsMembership: boolean;
    membershipGrantsEnrollment: boolean;
    autoKickOnRevoke: boolean;
    enforceMembership: boolean;
  }) => void;
}

function BindingCard({ binding, isPending, onDelete, onFlagChange }: CardProps) {
  const isChannel = binding.chatType === "CHANNEL";

  return (
    <li className="rounded-md border p-4">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0 space-y-1">
          <div className="flex items-center gap-2">
            <span className="font-medium">{binding.chatTitle ?? "Без названия"}</span>
            <span className="rounded bg-muted px-2 py-0.5 text-xs uppercase text-muted-foreground">
              {binding.chatType}
            </span>
          </div>
          <p className="text-xs text-muted-foreground">ID: {binding.telegramChatId}</p>
          <a
            href={binding.inviteLink}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
          >
            <Icons.externalLink className="h-3 w-3" />
            Invite link
          </a>
        </div>

        <DeleteConfirmDialog
          title="Отвязать чат?"
          description="Существующих участников из чата это не удалит. Просто прекратится автоматическое управление доступом."
          confirmLabel="Отвязать"
          onConfirm={onDelete}
          trigger={
            <Button variant="ghost" size="icon" disabled={isPending}>
              <Icons.delete className="h-4 w-4" />
            </Button>
          }
        />
      </div>

      <div className="mt-4 grid gap-2 text-sm">
        <FlagRow
          label="Получил план → доступ в чат"
          checked={binding.enrollmentGrantsMembership}
          disabled={isPending}
          onChange={(checked) =>
            onFlagChange({
              enrollmentGrantsMembership: checked,
              membershipGrantsEnrollment: binding.membershipGrantsEnrollment,
              autoKickOnRevoke: binding.autoKickOnRevoke,
              enforceMembership: binding.enforceMembership,
            })
          }
        />
        <FlagRow
          label={isChannel ? "Подписан на канал → доступ к плану" : "Состоит в чате → доступ к плану"}
          checked={binding.membershipGrantsEnrollment}
          disabled={isPending}
          onChange={(checked) =>
            onFlagChange({
              enrollmentGrantsMembership: binding.enrollmentGrantsMembership,
              membershipGrantsEnrollment: checked,
              autoKickOnRevoke: binding.autoKickOnRevoke,
              enforceMembership: binding.enforceMembership,
            })
          }
        />
        {binding.membershipGrantsEnrollment && (
          <AnnouncementStatus binding={binding} isChannel={isChannel} />
        )}
        <FlagRow
          label="Авто-кик при отзыве плана"
          checked={binding.autoKickOnRevoke}
          disabled={isPending}
          onChange={(checked) =>
            onFlagChange({
              enrollmentGrantsMembership: binding.enrollmentGrantsMembership,
              membershipGrantsEnrollment: binding.membershipGrantsEnrollment,
              autoKickOnRevoke: checked,
              enforceMembership: binding.enforceMembership,
            })
          }
        />
        <FlagRow
          label="Принудительная проверка членства"
          checked={binding.enforceMembership}
          disabled={isPending || isChannel}
          hint={isChannel ? "Не работает для каналов" : undefined}
          onChange={(checked) =>
            onFlagChange({
              enrollmentGrantsMembership: binding.enrollmentGrantsMembership,
              membershipGrantsEnrollment: binding.membershipGrantsEnrollment,
              autoKickOnRevoke: binding.autoKickOnRevoke,
              enforceMembership: checked,
            })
          }
        />
      </div>
    </li>
  );
}

function AnnouncementStatus({
  binding,
  isChannel,
}: {
  binding: ChatBindingDto;
  isChannel: boolean;
}) {
  const place = isChannel ? "канал" : "чат";
  const placeGenitive = isChannel ? "канала" : "чата";

  if (binding.announcementMessageId !== null) {
    return (
      <p className="rounded-md bg-muted/60 px-3 py-2 text-xs text-muted-foreground">
        ✅ Бот опубликовал и закрепил в {place === "чат" ? "чате" : "канале"} сообщение с кнопкой —
        участники могут привязать аккаунт и забрать доступ к плану.
      </p>
    );
  }

  return (
    <p className="rounded-md bg-amber-500/10 px-3 py-2 text-xs text-amber-700 dark:text-amber-400">
      ⚠️ Объявление с кнопкой ещё не опубликовано. Убедитесь, что бот — администратор {placeGenitive}{" "}
      с правом публиковать{isChannel ? " и закреплять" : ""} сообщения, затем переключите опцию заново.
    </p>
  );
}

function FlagRow({
  label,
  checked,
  disabled,
  hint,
  onChange,
}: {
  label: string;
  checked: boolean;
  disabled: boolean;
  hint?: string;
  onChange: (next: boolean) => void;
}) {
  return (
    <label className="flex items-center justify-between gap-3">
      <span className={`text-muted-foreground ${disabled ? "opacity-60" : ""}`}>
        {label}
        {hint && <span className="ml-2 text-xs italic">({hint})</span>}
      </span>
      <Switch checked={checked} disabled={disabled} onCheckedChange={onChange} />
    </label>
  );
}
