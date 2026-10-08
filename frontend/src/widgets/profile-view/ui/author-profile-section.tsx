"use client";

import type { MyProfile, UpdateMyAuthorProfileRequest } from "@/entities/profile";
import { useUpdateAuthorProfile } from "@/features/profile-manage";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { BookOpen, Briefcase } from "lucide-react";
import { useState } from "react";
import { FieldRow } from "./field-row";
import { EmptyProfileState, SectionHeader } from "./section-header";

export function AuthorProfileSection({ profile }: { profile: MyProfile }) {
  const { updateAuthorProfile, isPending: isAuthorPending } =
    useUpdateAuthorProfile();
  const [editing, setEditing] = useState(false);
  const [specialization, setSpecialization] = useState("");
  const [aboutAsAuthor, setAboutAsAuthor] = useState("");

  const author = profile.profiles?.author;
  const hasData = author?.specialization || author?.aboutAsAuthor;

  function startEdit() {
    setSpecialization(author?.specialization ?? "");
    setAboutAsAuthor(author?.aboutAsAuthor ?? "");
    setEditing(true);
  }

  function save() {
    const request: UpdateMyAuthorProfileRequest = {
      specialization: specialization.trim() || null,
      aboutAsAuthor: aboutAsAuthor.trim() || null,
    };
    updateAuthorProfile(request, { onSuccess: () => setEditing(false) });
  }

  if (!hasData && !editing) {
    return <EmptyProfileState roleName="автора" onEdit={startEdit} />;
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-muted-foreground uppercase tracking-wider">
          Профиль автора
        </h3>
        <SectionHeader
          editing={editing}
          onEdit={startEdit}
          onSave={save}
          onCancel={() => setEditing(false)}
          isPending={isAuthorPending}
        />
      </div>

      {editing ? (
        <div className="space-y-4 max-w-lg">
          <div className="space-y-2">
            <Label htmlFor="a-spec">Специализация</Label>
            <Input
              id="a-spec"
              value={specialization}
              onChange={(e) => setSpecialization(e.target.value)}
              placeholder="Backend .NET, Frontend React..."
              maxLength={255}
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="a-about">О себе как об авторе</Label>
            <Textarea
              id="a-about"
              value={aboutAsAuthor}
              onChange={(e) => setAboutAsAuthor(e.target.value)}
              rows={4}
              placeholder="Расскажите о своём опыте преподавания и создания курсов..."
              className="resize-none"
              maxLength={2000}
            />
          </div>
        </div>
      ) : (
        <div className="divide-y divide-border/50">
          <FieldRow
            label="Специализация"
            value={author?.specialization}
            icon={<Briefcase size={16} />}
          />
          <FieldRow
            label="О себе как авторе"
            value={author?.aboutAsAuthor}
            icon={<BookOpen size={16} />}
          />
        </div>
      )}
    </div>
  );
}
