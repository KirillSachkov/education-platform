"use client";

import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2 } from "lucide-react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { PLATFORM_ROLES } from "@/shared/config/roles";
import { useSetRoles } from "../model/use-set-roles";

const manageRolesSchema = z.object({
  roles: z.array(z.string()).min(1, "Выберите хотя бы одну роль"),
});

type ManageRolesFormData = z.infer<typeof manageRolesSchema>;

type Props = {
  userId: string;
  userEmail: string | null;
  currentRoles: string[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function ManageRolesDialog({
  userId,
  userEmail,
  currentRoles,
  open,
  onOpenChange,
}: Props) {
  const { setRoles, isPending } = useSetRoles();

  const { handleSubmit, setValue, watch } = useForm<ManageRolesFormData>({
    resolver: zodResolver(manageRolesSchema),
    defaultValues: { roles: currentRoles },
  });

  const selectedRoles = watch("roles");

  const toggleRole = (role: string) => {
    const current = selectedRoles ?? [];
    const next = current.includes(role)
      ? current.filter((r) => r !== role)
      : [...current, role];
    setValue("roles", next, { shouldValidate: true });
  };

  const onSubmit = async (data: ManageRolesFormData) => {
    await setRoles({ userId, request: { roles: data.roles } });
    onOpenChange(false);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle>Управление ролями</DialogTitle>
          <DialogDescription>{userEmail}</DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="flex flex-wrap gap-2 py-4">
            {PLATFORM_ROLES.map((role) => (
              <Button
                key={role.value}
                type="button"
                size="sm"
                variant={
                  selectedRoles?.includes(role.value) ? "default" : "outline"
                }
                onClick={() => toggleRole(role.value)}
              >
                {role.label}
              </Button>
            ))}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isPending}
            >
              Отмена
            </Button>
            <Button type="submit" disabled={isPending || selectedRoles.length === 0}>
              {isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
              Сохранить
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
