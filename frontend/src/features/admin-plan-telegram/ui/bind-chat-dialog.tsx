"use client";

import { FormDialog } from "@/shared/ui/components";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Switch } from "@/shared/ui/kit/switch";
import { useState } from "react";
import { useBindChat } from "../model/use-bind-chat";

interface Props {
  planId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function BindChatDialog({ planId, open, onOpenChange }: Props) {
  const { mutate } = useBindChat(planId);

  const [chatIdentifier, setChatIdentifier] = useState("");
  const [enrollmentGrantsMembership, setEnrollmentGrantsMembership] = useState(true);
  const [membershipGrantsEnrollment, setMembershipGrantsEnrollment] = useState(true);
  const [autoKickOnRevoke, setAutoKickOnRevoke] = useState(false);
  const [enforceMembership, setEnforceMembership] = useState(false);

  const reset = () => {
    setChatIdentifier("");
    setEnrollmentGrantsMembership(true);
    setMembershipGrantsEnrollment(true);
    setAutoKickOnRevoke(false);
    setEnforceMembership(false);
  };

  const handleClose = () => {
    reset();
    onOpenChange(false);
  };

  const onSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const id = chatIdentifier.trim();
    if (!id) return;

    mutate({
      chatIdentifier: id,
      enrollmentGrantsMembership,
      membershipGrantsEnrollment,
      autoKickOnRevoke,
      enforceMembership,
    });
    handleClose();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Привязать Telegram-чат к плану"
      description="Введите ID чата (отрицательное число для группы) или @username публичного чата/канала. Бот должен быть админом: для группы — права приглашать и ограничивать участников (can_invite_users, can_restrict_members); для канала — приглашать и публиковать/закреплять сообщения (can_restrict_members не требуется)."
      onSubmit={onSubmit}
      onCancel={handleClose}
      submitLabel="Привязать"
      submitDisabled={!chatIdentifier.trim()}
    >
      <div className="space-y-2">
        <Label htmlFor="chat-id">Chat ID или @username</Label>
        <Input
          id="chat-id"
          value={chatIdentifier}
          onChange={(e) => setChatIdentifier(e.target.value)}
          placeholder="-1001234567890 или @my_plan_chat"
        />
      </div>

      <div className="flex items-start justify-between gap-4 rounded-md border p-3">
        <div className="space-y-1">
          <Label htmlFor="grant-membership" className="font-medium">
            Получил план → доступ в чат
          </Label>
          <p className="text-xs text-muted-foreground">
            При выдаче plan-grant юзеру отправляется invite link в личку. Работает и для групп, и
            для каналов.
          </p>
        </div>
        <Switch
          id="grant-membership"
          checked={enrollmentGrantsMembership}
          onCheckedChange={setEnrollmentGrantsMembership}
        />
      </div>

      <div className="flex items-start justify-between gap-4 rounded-md border p-3">
        <div className="space-y-1">
          <Label htmlFor="grant-enrollment" className="font-medium">
            Состоит в чате/канале → доступ к плану
          </Label>
          <p className="text-xs text-muted-foreground">
            Бот публикует и закрепляет сообщение с кнопкой; участник нажимает её, привязывает аккаунт
            — бот проверяет членство и выдаёт grant на план. Работает и для групп, и для{" "}
            <strong>закрытых каналов</strong>. Для канала бот должен быть его администратором с правом
            публиковать и закреплять сообщения — членство проверяется после клика по кнопке.
          </p>
        </div>
        <Switch
          id="grant-enrollment"
          checked={membershipGrantsEnrollment}
          onCheckedChange={setMembershipGrantsEnrollment}
        />
      </div>

      <div className="flex items-start justify-between gap-4 rounded-md border p-3">
        <div className="space-y-1">
          <Label htmlFor="auto-kick" className="font-medium">
            Авто-кик при отзыве плана
          </Label>
          <p className="text-xs text-muted-foreground">
            При revoke / expire plan-grant юзера выкидывает из чата (ban + unban). По умолчанию
            выкл.
          </p>
        </div>
        <Switch
          id="auto-kick"
          checked={autoKickOnRevoke}
          onCheckedChange={setAutoKickOnRevoke}
        />
      </div>

      <div className="flex items-start justify-between gap-4 rounded-md border p-3">
        <div className="space-y-1">
          <Label htmlFor="enforce-membership" className="font-medium">
            Принудительная проверка членства
          </Label>
          <p className="text-xs text-muted-foreground">
            При ручном добавлении юзера (админом) бот проверяет plan-grant и кикает не-членов.{" "}
            <strong>Только для групп</strong>; opt-in (выключено по умолчанию — социально
            болезненно).
          </p>
        </div>
        <Switch
          id="enforce-membership"
          checked={enforceMembership}
          onCheckedChange={setEnforceMembership}
        />
      </div>
    </FormDialog>
  );
}
