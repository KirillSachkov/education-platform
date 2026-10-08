"use client";

import type { MyProfile } from "@/entities/profile";
import { ROLES, useRoles } from "@/shared/auth";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/ui/kit/card";
import { Separator } from "@/shared/ui/kit/separator";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/ui/kit/tabs";
import {
  Briefcase,
  ShieldCheck,
  User,
} from "lucide-react";
import { useSession } from "next-auth/react";
import { AccountInfoForm } from "./account-info-form";
import { AuthorProfileForm } from "./author-profile-form";
import { BaseProfileForm } from "./base-profile-form";
import { ReviewerProfileForm } from "./reviewer-profile-form";

type Props = {
  profile: MyProfile;
};

export function ProfileManage({ profile }: Props) {
  const { data: session } = useSession();
  const { hasRole } = useRoles();
  const isAdmin = hasRole(ROLES.ADMIN) || hasRole(ROLES.OWNER);
  const hasAuthorRole = hasRole(ROLES.AUTHOR) || hasRole(ROLES.EDITOR) || isAdmin;
  const hasReviewerRole = hasRole(ROLES.MODERATOR) || isAdmin;

  const roleTabs = [
    hasAuthorRole && {
      id: "author",
      label: "Автор",
      icon: <Briefcase size={16} />,
    },
    hasReviewerRole && {
      id: "reviewer",
      label: "Проверяющий",
      icon: <ShieldCheck size={16} />,
    },
  ].filter(Boolean) as { id: string; label: string; icon: React.ReactNode }[];

  const defaultTab = roleTabs[0]?.id;

  return (
    <div className="space-y-6">
      {/* ── Header ──────────────────────────────────────────────── */}
      <Card className="relative overflow-hidden">
        <div className="absolute top-0 inset-x-0 h-0.5 bg-gradient-primary" />
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <User size={20} />
            Настройки профиля
          </CardTitle>
          <CardDescription className="pt-1">
            Изменение профиля выполняется только внутри платформы.
            Восстановление доступа доступно на странице входа.
          </CardDescription>
        </CardHeader>
      </Card>

      {/* ── Account info ─────────────────────────────────────────── */}
      {session?.user && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Данные аккаунта</CardTitle>
            <CardDescription>
              Имя пользователя и отображаемое имя
            </CardDescription>
          </CardHeader>
          <Separator />
          <CardContent className="pt-6">
            <AccountInfoForm
              username={profile.username ?? ""}
              displayName={profile.displayName ?? ""}
            />
          </CardContent>
        </Card>
      )}

      {/* ── Base profile ────────────────────────────────────────── */}
      <Card>
        <CardHeader>
          <CardTitle className="text-base">Базовый профиль</CardTitle>
          <CardDescription>
            Доступно всем авторизованным пользователям
          </CardDescription>
        </CardHeader>
        <Separator />
        <CardContent className="pt-6">
          <BaseProfileForm profile={profile} />
        </CardContent>
      </Card>

      {/* ── Role profiles ───────────────────────────────────────── */}
      {roleTabs.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle className="text-base">Ролевые профили</CardTitle>
            <CardDescription>
              Дополнительная информация в зависимости от ваших ролей на
              платформе
            </CardDescription>
          </CardHeader>
          <Separator />
          <CardContent className="pt-6">
            <Tabs defaultValue={defaultTab}>
              <TabsList className="w-full justify-start mb-6">
                {roleTabs.map((tab) => (
                  <TabsTrigger key={tab.id} value={tab.id} className="gap-1.5">
                    {tab.icon}
                    {tab.label}
                  </TabsTrigger>
                ))}
              </TabsList>

              {hasAuthorRole && (
                <TabsContent value="author">
                  <AuthorProfileForm profile={profile} />
                </TabsContent>
              )}

              {hasReviewerRole && (
                <TabsContent value="reviewer">
                  <ReviewerProfileForm profile={profile} />
                </TabsContent>
              )}
            </Tabs>
          </CardContent>
        </Card>
      )}

    </div>
  );
}
