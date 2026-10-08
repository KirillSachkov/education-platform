"use client";

import type { TagId } from "@/entities/tag";
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
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Icons } from "@/shared/ui/icons";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { type UpdateTagFormData, updateTagSchema } from "../model/schemas";
import { useUpdateTag } from "../model/use-update-tag";

interface RenameTagDialogProps {
  tagId: TagId;
  tagTitle: string;
  variant?: "card" | "detail" | "icon";
}

export function RenameTagDialog({
  tagId,
  tagTitle,
  variant = "card",
}: RenameTagDialogProps) {
  const [open, setOpen] = useState(false);
  const { updateTag } = useUpdateTag();

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<UpdateTagFormData>({
    resolver: zodResolver(updateTagSchema),
    defaultValues: {
      title: tagTitle,
    },
  });

  const onOpenChange = (nextOpen: boolean) => {
    setOpen(nextOpen);
    if (nextOpen) {
      reset({ title: tagTitle });
    }
  };

  // Fire-and-forget: закрываем форму сразу, тосты из useUpdateTag.
  const onSubmit = (values: UpdateTagFormData) => {
    void updateTag({
      tagId,
      request: {
        title: values.title.trim(),
      },
    });
    setOpen(false);
  };

  const trigger =
    variant === "detail" ? (
      <Button variant="outline" size="sm">
        <Icons.edit size={14} />
        Переименовать
      </Button>
    ) : variant === "icon" ? (
      <Button
        variant="ghost"
        size="icon-sm"
        className="text-muted-foreground hover:text-foreground"
      >
        <Icons.edit className="size-4" />
      </Button>
    ) : (
      <Button variant="ghost" size="sm" className="text-xs">
        <Icons.edit size={13} />
        Переименовать
      </Button>
    );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>

      <DialogContent className="sm:max-w-[440px]">
        <DialogHeader>
          <DialogTitle>Переименовать тег</DialogTitle>
          <DialogDescription>
            Укажите новое название для тега «{tagTitle}».
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)}>
          <div className="space-y-2 py-4">
            <Label htmlFor={`rename-tag-title-${tagId}`}>Название</Label>
            <Input
              id={`rename-tag-title-${tagId}`}
              placeholder="Введите новое название"
              {...register("title")}
            />
            {errors.title && (
              <p className="text-sm text-destructive">{errors.title.message}</p>
            )}
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setOpen(false)}
            >
              Отмена
            </Button>
            <Button type="submit">Сохранить</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
