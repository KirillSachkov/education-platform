"use client";

import type { MyProfile } from "@/entities/profile";
import { Button } from "@/shared/ui/kit/button";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { zodResolver } from "@hookform/resolvers/zod";
import { Loader2, Save } from "lucide-react";
import { useForm } from "react-hook-form";
import {
  baseSchema,
  normalizeText,
  type BaseFormValues,
} from "../model/schemas";
import { useUpdateBaseProfile } from "../model/use-update-base-profile";

type Props = {
  profile: MyProfile;
};

export function BaseProfileForm({ profile }: Props) {
  const { updateBaseProfile, isPending } = useUpdateBaseProfile();

  const form = useForm<BaseFormValues>({
    resolver: zodResolver(baseSchema),
    values: {
      bio: profile.bio ?? "",
    },
  });

  const onSubmit = (values: BaseFormValues) => {
    updateBaseProfile({
      bio: normalizeText(values.bio),
    });
  };

  const bio = form.watch("bio") ?? "";

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor="base-bio">О себе</Label>
          <span className="text-xs text-muted-foreground tabular-nums">
            {bio.length}/1000
          </span>
        </div>
        <Textarea
          id="base-bio"
          rows={4}
          placeholder="Расскажите кратко о себе..."
          className="resize-none"
          {...form.register("bio")}
        />
        {form.formState.errors.bio && (
          <p className="text-sm text-destructive">
            {form.formState.errors.bio.message}
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
