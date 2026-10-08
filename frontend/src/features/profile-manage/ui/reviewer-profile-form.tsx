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
  normalizeText,
  reviewerSchema,
  type ReviewerFormValues,
} from "../model/schemas";
import { useUpdateReviewerProfile } from "../model/use-update-reviewer-profile";

type Props = {
  profile: MyProfile;
};

export function ReviewerProfileForm({ profile }: Props) {
  const { updateReviewerProfile, isPending } = useUpdateReviewerProfile();
  const reviewer = profile.profiles?.reviewer;

  const form = useForm<ReviewerFormValues>({
    resolver: zodResolver(reviewerSchema),
    values: {
      reviewCapacity: reviewer?.reviewCapacity?.toString() ?? "",
      expertise: reviewer?.expertise ?? "",
    },
  });

  const onSubmit = (values: ReviewerFormValues) => {
    const reviewCapacityValue =
      values.reviewCapacity && values.reviewCapacity.trim().length > 0
        ? Number(values.reviewCapacity)
        : null;

    updateReviewerProfile({
      reviewCapacity: reviewCapacityValue,
      expertise: normalizeText(values.expertise),
    });
  };

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-5">
      <div className="space-y-2">
        <Label htmlFor="reviewer-capacity">Лимит задач на ревью</Label>
        <Input
          id="reviewer-capacity"
          type="number"
          min={0}
          placeholder="5"
          {...form.register("reviewCapacity")}
        />
        {form.formState.errors.reviewCapacity && (
          <p className="text-sm text-destructive">
            {form.formState.errors.reviewCapacity.message}
          </p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="reviewer-expertise">Экспертиза</Label>
        <Textarea
          id="reviewer-expertise"
          rows={3}
          placeholder="В каких областях вы можете проверять задания?"
          className="resize-none"
          {...form.register("expertise")}
        />
        {form.formState.errors.expertise && (
          <p className="text-sm text-destructive">
            {form.formState.errors.expertise.message}
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
