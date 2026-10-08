"use client";

import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { FormDialog } from "@/shared/ui/components";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { createTagSchema, type CreateTagFormData } from "../model/schemas";
import { useCreateTag } from "../model/use-create-tag";

const defaultValues: CreateTagFormData = {
  title: "",
};

interface CreateTagDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function CreateTagDialog({ open, onOpenChange }: CreateTagDialogProps) {
  const { createTag } = useCreateTag();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateTagFormData>({
    resolver: zodResolver(createTagSchema),
    defaultValues,
  });

  const handleClose = () => {
    reset(defaultValues);
    onOpenChange(false);
  };

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) {
      reset(defaultValues);
    }
    onOpenChange(nextOpen);
  };

  // Fire-and-forget: закрываем форму сразу, тосты из useCreateTag.
  const onSubmit = (values: CreateTagFormData) => {
    void createTag({ title: values.title });
    handleClose();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={handleOpenChange}
      title="Создать тег"
      description="Добавьте новый тег для дальнейшего использования в контенте."
      onSubmit={handleSubmit(onSubmit)}
      onCancel={handleClose}
      submitLabel="Создать"
      contentClassName="sm:max-w-[440px]"
      bodyClassName="space-y-2"
    >
      <Label htmlFor="tag-title">Название</Label>
      <Input id="tag-title" placeholder="Например: C#" {...register("title")} />
      {errors.title && (
        <p className="text-sm text-destructive">{errors.title.message}</p>
      )}
    </FormDialog>
  );
}
