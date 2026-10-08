"use client";

import type { MyProfile } from "@/entities/profile";
import { AvatarUpload, useMyProfile, useUpdateAccountInfo } from "@/features/profile-manage";
import { type Role, ROLES, useRoles } from "@/shared/auth";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import { Input } from "@/shared/ui/kit/input";
import {
  Briefcase,
  Check,
  Edit2,
  ExternalLink,
  Github,
  Loader2,
  Mail,
  Shield,
  ShieldCheck,
  User,
  X,
} from "lucide-react";
import { useState } from "react";

import { AuthorProfileSection } from "./ui/author-profile-section";
import { BaseProfileSection } from "./ui/base-profile-section";
import { LevelProgressSection } from "./ui/level-progress-section";
import { ProfileSkeleton } from "./ui/profile-skeleton";
import { ReviewerProfileSection } from "./ui/reviewer-profile-section";

type RoleConfig = {
  key: string;
  label: string;
  icon: React.ReactNode;
  color: string;
  badgeLabel: string;
};

const ROLE_CONFIGS: Record<string, RoleConfig> = {
  [ROLES.PARTICIPANT]: {
    key: "participant",
    label: "Участник",
    icon: <User size={14} />,
    color: "text-muted-foreground",
    badgeLabel: "Участник",
  },
  [ROLES.AUTHOR]: {
    key: "author",
    label: "Автор",
    icon: <Briefcase size={14} />,
    color: "text-blue",
    badgeLabel: "Автор",
  },
  [ROLES.EDITOR]: {
    key: "editor",
    label: "Редактор",
    icon: <Briefcase size={14} />,
    color: "text-teal",
    badgeLabel: "Редактор",
  },
  [ROLES.MODERATOR]: {
    key: "reviewer",
    label: "Проверяющий",
    icon: <ShieldCheck size={14} />,
    color: "text-purple",
    badgeLabel: "Проверяющий",
  },
  [ROLES.ADMIN]: {
    key: "admin",
    label: "Администратор",
    icon: <Shield size={14} />,
    color: "text-orange",
    badgeLabel: "Администратор",
  },
  [ROLES.OWNER]: {
    key: "owner",
    label: "Владелец",
    icon: <Shield size={14} />,
    color: "text-amber",
    badgeLabel: "Владелец",
  },
};

/**
 * Public-facing profile card. Account-level settings (GitHub/Telegram link, sessions,
 * notification preferences, etc.) живут в /settings/* — здесь только то, что юзер
 * сам показывает другим: имя, аватар, био, уровень/XP, role-views.
 */
export function ProfileView() {
  const { profile, isPending, error } = useMyProfile();
  const { hasRole } = useRoles();

  if (isPending) {
    return <ProfileSkeleton />;
  }

  if (error || !profile) {
    return (
      <div className="mx-auto w-full max-w-3xl px-4 sm:px-8 py-10">
        <Card className="py-12 px-8 text-center">
          <div className="size-14 rounded-full bg-red-dim flex items-center justify-center mx-auto mb-4">
            <X size={24} className="text-red" />
          </div>
          <p className="text-sm text-muted-foreground mb-4">Не удалось загрузить профиль</p>
          <Button variant="outline" onClick={() => window.location.reload()}>
            Попробовать снова
          </Button>
        </Card>
      </div>
    );
  }

  return <ProfileContent profile={profile} hasRole={hasRole} />;
}

function ProfileContent({
  profile,
  hasRole,
}: {
  profile: MyProfile;
  hasRole: (role: Role) => boolean;
}) {
  const isAdmin = hasRole(ROLES.ADMIN) || hasRole(ROLES.OWNER);
  const showAuthor = hasRole(ROLES.AUTHOR) || hasRole(ROLES.EDITOR) || isAdmin;
  const showReviewer = hasRole(ROLES.MODERATOR) || isAdmin;

  const gitHubUrl = profile.profiles?.student?.gitHubUrl;

  const roleTabs = [
    showAuthor && { id: "author", label: "Автор", icon: <Briefcase size={16} /> },
    showReviewer && { id: "reviewer", label: "Проверяющий", icon: <ShieldCheck size={16} /> },
  ].filter(Boolean) as { id: string; label: string; icon: React.ReactNode }[];

  const defaultTab = roleTabs.length > 0 ? roleTabs[0].id : "base";

  return (
    <div className="mx-auto w-full max-w-3xl px-4 sm:px-8 py-8 sm:py-10 space-y-8">
      <ProfileHero profile={profile} isAdmin={isAdmin} gitHubUrl={gitHubUrl} />

      <SectionCard>
        <BaseProfileSection profile={profile} />
      </SectionCard>

      <LevelProgressSection />

      {roleTabs.length > 0 && (
        <SectionCard>
          <Tabs defaultValue={defaultTab}>
            <TabsList className="w-full justify-start mb-5 gap-1">
              {roleTabs.map((tab) => (
                <TabsTrigger key={tab.id} value={tab.id} className="gap-1.5">
                  {tab.icon}
                  {tab.label}
                </TabsTrigger>
              ))}
            </TabsList>

            {showAuthor && (
              <TabsContent value="author">
                <AuthorProfileSection profile={profile} />
              </TabsContent>
            )}

            {showReviewer && (
              <TabsContent value="reviewer">
                <ReviewerProfileSection profile={profile} />
              </TabsContent>
            )}
          </Tabs>
        </SectionCard>
      )}

      {roleTabs.length === 0 && (
        <Card className="py-12 px-8 text-center">
          <div className="size-14 rounded-full bg-muted flex items-center justify-center mx-auto mb-4">
            <User size={24} className="text-muted-foreground" />
          </div>
          <p className="text-sm text-muted-foreground">
            У вас пока нет дополнительных ролей на платформе
          </p>
          <p className="text-xs text-muted-foreground/60 mt-1">
            Свяжитесь с администратором для получения роли студента, автора или проверяющего
          </p>
        </Card>
      )}
    </div>
  );
}

/* ────────────────────────────── Hero ────────────────────────────── */

function ProfileHero({
  profile,
  isAdmin,
  gitHubUrl,
}: {
  profile: MyProfile;
  isAdmin: boolean;
  gitHubUrl: string | null | undefined;
}) {
  const { updateAccountInfo, isPending } = useUpdateAccountInfo();
  const [editing, setEditing] = useState(false);
  const [displayName, setDisplayName] = useState("");
  const [username, setUsername] = useState("");

  function startEdit() {
    setDisplayName(profile.displayName ?? "");
    setUsername(profile.username);
    setEditing(true);
  }

  function save() {
    const trimmedDisplayName = displayName.trim();
    const trimmedUsername = username.trim();

    const hasChanges =
      trimmedDisplayName !== (profile.displayName ?? "") || trimmedUsername !== profile.username;

    if (!hasChanges) {
      setEditing(false);
      return;
    }

    updateAccountInfo(
      { username: trimmedUsername, displayName: trimmedDisplayName },
      { onSuccess: () => setEditing(false) },
    );
  }

  return (
    <SectionCard>
      <div className="flex flex-col sm:flex-row items-start gap-5 sm:gap-6">
        <AvatarUpload userId={profile.id} avatarId={profile.avatarId} name={profile.name} />

        <div className="flex-1 min-w-0 w-full">
          {editing ? (
            <div className="space-y-3">
              <div>
                <label className="text-xs text-muted-foreground mb-1 block">Отображаемое имя</label>
                <Input
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  placeholder="Имя Фамилия"
                  maxLength={150}
                  autoFocus
                />
              </div>
              <div>
                <label className="text-xs text-muted-foreground mb-1 block">Имя пользователя</label>
                <Input
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  placeholder="username"
                  maxLength={150}
                />
              </div>
              <div className="flex items-center gap-2 pt-1">
                <Button size="sm" onClick={save} disabled={isPending}>
                  {isPending ? (
                    <Loader2 size={14} className="mr-1.5 animate-spin" />
                  ) : (
                    <Check size={14} className="mr-1.5" />
                  )}
                  {isPending ? "Сохранение..." : "Сохранить"}
                </Button>
                <Button
                  size="sm"
                  variant="ghost"
                  onClick={() => setEditing(false)}
                  disabled={isPending}
                >
                  Отмена
                </Button>
              </div>
            </div>
          ) : (
            <>
              <div className="flex items-center gap-2.5 flex-wrap">
                <h1 className="min-w-0 max-w-full text-2xl font-bold tracking-tight wrap-anywhere">
                  {profile.displayName || profile.username}
                </h1>
                {isAdmin && (
                  <Badge className="bg-orange-dim text-orange border-0 gap-1">
                    <Shield size={12} />
                    Админ
                  </Badge>
                )}
                <Button
                  variant="ghost"
                  size="icon"
                  className="size-7 text-muted-foreground hover:text-foreground"
                  onClick={startEdit}
                  aria-label="Редактировать профиль"
                >
                  <Edit2 size={13} />
                </Button>
              </div>

              {profile.displayName && (
                <p className="text-sm text-muted-foreground mt-0.5">@{profile.username}</p>
              )}

              <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1.5 text-sm text-muted-foreground">
                <span className="inline-flex items-center gap-1.5">
                  <Mail size={13} />
                  {profile.email}
                </span>
                {gitHubUrl && (
                  <a
                    href={gitHubUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center gap-1.5 hover:text-foreground transition-colors max-w-[200px]"
                  >
                    <Github size={13} className="shrink-0" />
                    <span className="truncate">{gitHubUrl.replace("https://github.com/", "")}</span>
                    <ExternalLink size={10} className="opacity-50 shrink-0" />
                  </a>
                )}
              </div>

              <div className="mt-3 flex flex-wrap gap-1.5">
                {(() => {
                  const visibleRoles = [...new Set(profile.roles)].filter(
                    (group) => Boolean(ROLE_CONFIGS[group]) && group !== ROLES.ADMIN,
                  );
                  const rolesToRender =
                    visibleRoles.length > 0 ? visibleRoles : [ROLES.PARTICIPANT];
                  return rolesToRender.map((group) => {
                    const config = ROLE_CONFIGS[group];
                    if (!config) return null;
                    return (
                      <Badge key={group} variant="secondary" className={`gap-1 ${config.color}`}>
                        {config.icon}
                        {config.badgeLabel}
                      </Badge>
                    );
                  });
                })()}
              </div>
            </>
          )}
        </div>
      </div>
    </SectionCard>
  );
}

/* ─────────────────────────── Primitives ─────────────────────────── */

function SectionCard({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <div className={`rounded-2xl border border-border/50 bg-card/40 p-5 sm:p-6 ${className ?? ""}`}>
      {children}
    </div>
  );
}
