"use client";

import type { MyProfile } from "@/entities/profile";
import { Button } from "@/shared/ui/kit/button";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Save } from "lucide-react";
import { useForm } from "react-hook-form";
import {
  authorSchema,
  normalizeText,
  type AuthorFormValues,
} from "../model/schemas";
import { useUpdateAuthorProfile } from "../model/use-update-author-profile";

type Props = {
  profile: MyProfile;
};

export function AuthorProfileForm({ profile }: Props) {
  const { updateAuthorProfile, isPending } = useUpdateAuthorProfile();
  const author = profile.profiles?.author;

  const form = useForm<AuthorFormValues>({
    resolver: zodResolver(authorSchema),
    values: {
      specialization: author?.specialization ?? "",
      aboutAsAuthor: author?.aboutAsAuthor ?? "",
    },
  });

  const onSubmit = (values: AuthorFormValues) => {
    updateAuthorProfile({
      specialization: normalizeText(values.specialization),
      aboutAsAuthor: normalizeText(values.aboutAsAuthor),
    });
  };

  const aboutAsAuthor = form.watch("aboutAsAuthor") ?? "";

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
      <div className="space-y-2">
        <Label htmlFor="author-specialization">Специализация</Label>
        <Input
          id="author-specialization"
          placeholder="Backend .NET, Frontend React..."
          {...form.register("specialization")}
        />
        {form.formState.errors.specialization && (
          <p className="text-sm text-destructive">
            {form.formState.errors.specialization.message}
          </p>
        )}
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor="author-about">О себе как об авторе</Label>
          <span className="text-xs text-muted-foreground tabular-nums">
            {aboutAsAuthor.length}/2000
          </span>
        </div>
        <Textarea
          id="author-about"
          rows={4}
          placeholder="Расскажите о своём опыте преподавания и создания курсов..."
          className="resize-none"
          {...form.register("aboutAsAuthor")}
        />
        {form.formState.errors.aboutAsAuthor && (
          <p className="text-sm text-destructive">
            {form.formState.errors.aboutAsAuthor.message}
          </p>
        )}
      </div>

      <Button
        type="submit"
        disabled={isPending || !form.formState.isDirty}
      >
        {isPending ? (
          <Loader2 size={16} className="mr-2 animate-spin" />
        ) : (
          <Save size={16} className="mr-2" />
        )}
        {isPending ? "Сохранение..." : "Сохранить"}
      </Button>
    </form>
  );
}
