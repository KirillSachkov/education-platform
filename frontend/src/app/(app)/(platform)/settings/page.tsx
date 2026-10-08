import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { Icons } from "@/shared/ui/icons";

export const metadata: Metadata = {
  title: "Настройки",
};

// Известные значения query-параметров (см. AccountStatusBanner.MESSAGES). Чужие/невалидные
// значения молча отбрасываем, чтобы не пробрасывать произвольный пользовательский ввод
// в URL — даже несмотря на encode и closed allowlist в самом баннере.
const SYNC_VALUES = new Set(["success", "no_orgs", "error", "email_mismatch"]);
const ACCOUNT_VALUES = new Set([
  "github-linked",
  "github-already-linked",
  "github-link-conflict",
  "github-link-failed",
]);

/**
 * Index-страница `/settings`.
 *
 * - Если пришли с GitHub-sync (`?sync=...`) или GitHub-link callback (`?account=...`),
 *   перебрасываем на `/settings/integrations`, чтобы баннер отрендерился в нужном разделе.
 * - На мобильных layout прячет content-колонку → юзер видит только sidebar-список.
 * - На десктопе показываем placeholder в content-колонке.
 */
export default async function SettingsRoute({
  searchParams,
}: {
  searchParams: Promise<{ sync?: string; account?: string }>;
}) {
  const params = await searchParams;
  if (params.sync && SYNC_VALUES.has(params.sync)) {
    redirect(`/settings/integrations?sync=${params.sync}`);
  }
  if (params.account && ACCOUNT_VALUES.has(params.account)) {
    redirect(`/settings/integrations?account=${params.account}`);
  }

  return (
    <div className="hidden md:flex min-h-[40vh] items-center justify-center rounded-2xl border border-dashed border-border/50 bg-card/20 p-10 text-center">
      <div className="space-y-2">
        <span className="mx-auto flex size-12 items-center justify-center rounded-2xl bg-muted/60 text-muted-foreground">
          <Icons.settings size={20} />
        </span>
        <p className="text-sm font-medium text-foreground">Выберите раздел</p>
        <p className="text-xs text-muted-foreground">Все настройки слева в боковом меню</p>
      </div>
    </div>
  );
}
