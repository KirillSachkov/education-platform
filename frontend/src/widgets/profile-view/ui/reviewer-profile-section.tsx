"use client";

import type { MyProfile, UpdateMyReviewerProfileRequest } from "@/entities/profile";
import { useUpdateReviewerProfile } from "@/features/profile-manage";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { ShieldCheck, Target } from "lucide-react";
import { useState } from "react";
import { FieldRow } from "./field-row";
import { EmptyProfileState, SectionHeader } from "./section-header";

export function ReviewerProfileSection({ profile }: { profile: MyProfile }) {
  const { updateReviewerProfile, isPending: isReviewerPending } =
    useUpdateReviewerProfile();
  const [editing, setEditing] = useState(false);
  const [reviewCapacity, setReviewCapacity] = useState("");
  const [expertise, setExpertise] = useState("");

  const reviewer = profile.profiles?.reviewer;
  const hasData = reviewer?.reviewCapacity != null || reviewer?.expertise;

  function startEdit() {
    setReviewCapacity(reviewer?.reviewCapacity?.toString() ?? "");
    setExpertise(reviewer?.expertise ?? "");
    setEditing(true);
  }

  function save() {
    const capacityValue = reviewCapacity.trim() ? Number(reviewCapacity) : null;

    const request: UpdateMyReviewerProfileRequest = {
      reviewCapacity: capacityValue,
      expertise: expertise.trim() || null,
    };
    updateReviewerProfile(request, { onSuccess: () => setEditing(false) });
  }

  if (!hasData && !editing) {
    return <EmptyProfileState roleName="проверяющего" onEdit={startEdit} />;
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Профиль проверяющего
        </h3>
        <SectionHeader
          editing={editing}
          onEdit={startEdit}
          onSave={save}
          onCancel={() => setEditing(false)}
          isPending={isReviewerPending}
        />
      </div>

      {editing ? (
        <div className="space-y-4 max-w-lg">
          <div className="space-y-2">
            <Label htmlFor="r-capacity">Лимит задач на ревью</Label>
            <Input
              id="r-capacity"
              type="number"
              min={0}
              value={reviewCapacity}
              onChange={(e) => setReviewCapacity(e.target.value)}
              placeholder="5"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="r-expertise">Экспертиза</Label>
            <Textarea
              id="r-expertise"
              value={expertise}
              onChange={(e) => setExpertise(e.target.value)}
              rows={3}
              placeholder="В каких областях вы можете проверять задания?"
              className="resize-none"
              maxLength={500}
            />
          </div>
        </div>
      ) : (
        <div className="divide-y divide-border/50">
          <FieldRow
            label="Лимит задач на ревью"
            value={reviewer?.reviewCapacity}
            icon={<Target size={16} />}
          />
          <FieldRow
            label="Экспертиза"
            value={reviewer?.expertise}
            icon={<ShieldCheck size={16} />}
          />
        </div>
      )}
    </div>
  );
}
