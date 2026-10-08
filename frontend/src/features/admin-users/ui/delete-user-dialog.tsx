"use client";

import { DeleteConfirmDialog } from "@/shared/ui/components";
import { useDeleteUser } from "../model/use-delete-user";

type Props = {
  userId: string;
  userEmail: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function DeleteUserDialog({
  userId,
  userEmail,
  open,
  onOpenChange,
}: Props) {
  const { deleteUser, isPending } = useDeleteUser();

  return (
    <DeleteConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      isPending={isPending}
      title="Удалить пользователя?"
      description={
        <>
          Пользователь <strong>{userEmail}</strong> будет удалён навсегда. Это
          действие нельзя отменить.
        </>
      }
      onConfirm={async () => {
        await deleteUser(userId);
      }}
    />
  );
}
