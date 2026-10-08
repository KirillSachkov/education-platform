"use client";

import { TagsField } from "@/entities/tag";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { FormDialog } from "@/shared/ui/components";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { moduleSchema, type ModuleFormData } from "../model/schemas";

const defaultValues: ModuleFormData = { title: "", description: "" };

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (data: { title: string; description: string | null; tags: string[] }) => void | Promise<unknown>;
};

export function CreateModuleDialog({ open, onOpenChange, onSubmit: onSubmitProp }: Props) {
  const [tags, setTags] = useState<string[]>([]);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<ModuleFormData>({
    resolver: zodResolver(moduleSchema),
    defaultValues,
  });

  const handleClose = () => {
    reset(defaultValues);
    setTags([]);
    onOpenChange(false);
  };

  // Fire-and-forget: закрываем форму сразу, тосты (success/error) приходят
  // из mutation hook. Возможные сетевые ошибки видны в тосте.
  const onSubmit = (data: ModuleFormData) => {
    onSubmitProp({ title: data.title, description: data.description || null, tags });
    handleClose();
  };

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Создание модуля"
      description="Заполните форму для создания нового модуля"
      onSubmit={handleSubmit(onSubmit)}
      onCancel={handleClose}
      submitLabel="Создать"
    >
      <div className="space-y-2">
        <Label htmlFor="module-title">Название</Label>
        <Input
          id="module-title"
          {...register("title")}
          placeholder="Введите название модуля"
        />
        {errors.title && (
          <p className="text-sm text-destructive">{errors.title.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="module-description">Описание</Label>
        <Textarea
          id="module-description"
          {...register("description")}
          placeholder="Краткое описание модуля (необязательно)"
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-destructive">
            {errors.description.message}
          </p>
        )}
      </div>

      <div className="space-y-2">
        <Label>Теги</Label>
        <TagsField value={tags} onChange={setTags} />
      </div>
    </FormDialog>
  );
}
