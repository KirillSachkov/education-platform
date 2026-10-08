"use client";

import { UserSearchCombobox, type AdminUserSummary } from "@/entities/user";
import { ROLES } from "@/shared/auth";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/shared/ui/kit/dialog";
import { Icons } from "@/shared/ui/icons";
import { useState } from "react";
import { useTransferCourseAuthor } from "../model/use-transfer-course-author";

interface TransferCourseAuthorDialogProps {
  courseId: string;
  courseTitle: string;
}

/**
 * Admin/moderator action — transfer a course (and its content) to another author (#587).
 * The picker is restricted to users holding the `platform-author` role. Visibility is
 * gated by the caller (`<Can roles={[MODERATOR, ADMIN]}>` at the page level).
 */
export function TransferCourseAuthorDialog({
  courseId,
  courseTitle,
}: TransferCourseAuthorDialogProps) {
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<AdminUserSummary[]>([]);
  const { transferCourseAuthor, isPending } = useTransferCourseAuthor();

  const target = selected[0];

  const handleConfirm = async () => {
    if (!target) return;
    try {
      await transferCourseAuthor({ courseId, newAuthorId: target.id });
      setOpen(false);
      setSelected([]);
    } catch {
      // Ошибка уже показана toast'ом в хуке — диалог оставляем открытым.
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        setOpen(next);
        if (!next) setSelected([]);
      }}
    >
      <DialogTrigger asChild>
        <Button variant="outline" size="sm">
          <Icons.arrowSwap className="size-3.5" />
          <span className="hidden sm:inline">Передать автору</span>
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Передать курс другому автору</DialogTitle>
          <DialogDescription>
            «{courseTitle}» и всё его содержимое перейдёт выбранному автору — он сможет
            управлять курсом и получать уведомления по нему. Доступ учеников не изменится.
          </DialogDescription>
        </DialogHeader>

        <UserSearchCombobox
          singleSelect
          showAllByDefault
          role={ROLES.AUTHOR}
          selectedUsers={selected}
          onSelectedUsersChange={setSelected}
          placeholder="Поиск автора по имени, username или Telegram…"
        />

        <DialogFooter>
          <Button variant="ghost" onClick={() => setOpen(false)} disabled={isPending}>
            Отмена
          </Button>
          <Button onClick={handleConfirm} disabled={!target || isPending}>
            {isPending ? <Icons.loading className="size-3.5 animate-spin" /> : null}
            Передать
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
