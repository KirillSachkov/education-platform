"use client";

import type { MyProfile } from "@/entities/profile";
import type { UpdateMyBaseProfileRequest } from "@/entities/profile";
import { useUpdateBaseProfile } from "@/features/profile-manage";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useState } from "react";
import { SectionHeader } from "./section-header";

export function BaseProfileSection({ profile }: { profile: MyProfile }) {
  const { updateBaseProfile, isPending } = useUpdateBaseProfile();
  const [editing, setEditing] = useState(false);
  const [bio, setBio] = useState("");

  function startEdit() {
    setBio(profile.bio ?? "");
    setEditing(true);
  }

  function save() {
    const request: UpdateMyBaseProfileRequest = {
      bio: bio.trim() || null,
    };
    updateBaseProfile(request, { onSuccess: () => setEditing(false) });
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          О себе
        </h3>
        <SectionHeader
          editing={editing}
          onEdit={startEdit}
          onSave={save}
          onCancel={() => setEditing(false)}
          isPending={isPending}
        />
      </div>

      {editing ? (
        <div className="space-y-2">
          <Textarea
            value={bio}
            onChange={(e) => setBio(e.target.value)}
            rows={4}
            placeholder="Расскажите о себе..."
            className="resize-none"
            maxLength={1000}
          />
          <p className="text-xs text-muted-foreground text-right">
            {bio.length}/1000
          </p>
        </div>
      ) : profile.bio ? (
        <p className="text-sm text-foreground/90 leading-relaxed whitespace-pre-wrap break-words">
          {profile.bio}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground/60 italic">
          Биография не указана
        </p>
      )}
    </div>
  );
}
