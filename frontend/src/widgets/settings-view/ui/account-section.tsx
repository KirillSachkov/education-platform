"use client";

import { useMyProfile, AccountInfoForm } from "@/features/profile-manage";
import { Icons } from "@/shared/ui/icons";
import { Skeleton } from "@/shared/ui/kit/skeleton";

/**
 * Раздел «Аккаунт» — username, отображаемое имя, email (read-only).
 * Удаление аккаунта появится здесь же, когда фича будет (TODO).
 */
export function AccountSection() {
  const { profile, isPending, error } = useMyProfile();

  if (isPending) {
    return (
      <SettingsCard>
        <Skeleton className="h-9 w-full max-w-sm" />
        <Skeleton className="h-9 w-full max-w-sm" />
        <Skeleton className="h-9 w-32" />
      </SettingsCard>
    );
  }

  if (error || !profile) {
    return (
      <SettingsCard>
        <p className="text-sm text-destructive">Не удалось загрузить профиль</p>
      </SettingsCard>
    );
  }

  return (
    <div className="space-y-6">
      <SettingsCard>
        <SectionTitle
          icon={<Icons.user className="size-4" />}
          title="Личные данные"
          description="Эти данные видны другим пользователям платформы"
        />
        <div className="mt-5">
          <AccountInfoForm username={profile.username} displayName={profile.displayName ?? ""} />
        </div>
      </SettingsCard>

      <SettingsCard>
        <SectionTitle
          icon={<Icons.mail className="size-4" />}
          title="Email"
          description="Используется для входа и системных писем"
        />
        <div className="mt-4 flex items-center gap-3 rounded-xl border border-border/50 bg-muted/30 px-4 py-3">
          <Icons.mail className="size-4 text-muted-foreground" />
          <p className="text-sm font-medium text-foreground">{profile.email}</p>
        </div>
        <p className="mt-3 text-xs text-muted-foreground">
          Изменение email пока недоступно. Напишите в поддержку, если нужно.
        </p>
      </SettingsCard>
    </div>
  );
}

function SettingsCard({ children }: { children: React.ReactNode }) {
  return (
    <section className="rounded-2xl border border-border/50 bg-card/40 p-5 sm:p-6">
      {children}
    </section>
  );
}

function SectionTitle({
  icon,
  title,
  description,
}: {
  icon: React.ReactNode;
  title: string;
  description: string;
}) {
  return (
    <header className="flex items-start gap-3">
      <span className="flex size-9 shrink-0 items-center justify-center rounded-xl bg-muted/60 text-muted-foreground">
        {icon}
      </span>
      <div>
        <h3 className="text-sm font-semibold text-foreground">{title}</h3>
        <p className="mt-0.5 text-xs text-muted-foreground/90">{description}</p>
      </div>
    </header>
  );
}
