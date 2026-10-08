"use client";

import { useState } from "react";
import type { AdminCampaignSlug } from "@/entities/admin-campaigns";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/shared/ui/kit/card";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { useRecipientCount } from "../model/use-recipient-count";
import { useRunCampaign } from "../model/use-run-campaign";
import { useSendTestCampaign } from "../model/use-send-test-campaign";

/**
 * Admin-only page /admin/campaigns. Управление платформенными кампаниями-рассылками:
 * «приглашение на тест уровня» (#554), «вход теперь по почте» и «привяжите аккаунты»
 * (#704, epic #696). Для каждой: размер аудитории + тестовая отправка себе + запуск.
 *
 * Permission gate стоит на layout (RequireRole atLeast=admin) + backend endpoints
 * (RequirePermissions Platform.ADMIN) — двойная защита.
 */

type CampaignConfig = {
  slug: AdminCampaignSlug;
  title: string;
  description: string;
  Icon: IconComponent;
  /** Toast после тестовой отправки — куда смотреть (почта vs in-app инбокс). */
  testSuccessMessage: string;
};

const CAMPAIGNS: CampaignConfig[] = [
  {
    slug: "level-test-invite",
    title: "Приглашение на тест уровня",
    description:
      "Письмо уйдёт всем пользователям с почтой и пригласит пройти публичный тест " +
      "уровня. Отписка работает штатно — повторный запуск пропускает уже приглашённых.",
    Icon: Icons.target,
    testSuccessMessage: "Письмо отправлено на вашу почту — проверьте",
  },
  {
    slug: "email-login-notice",
    title: "Вход теперь по почте",
    description:
      "Пользователям С привязкой GitHub: вход через GitHub отключён по закону, аккаунт " +
      "сохранён — вход по коду на почту. Email форсирован (критичное уведомление об " +
      "аккаунте — дойдёт даже при выключенном email-канале) + запись в инбоксе. " +
      "Повторный запуск пропускает уже уведомлённых.",
    Icon: Icons.mail,
    testSuccessMessage: "Письмо отправлено на вашу почту — проверьте",
  },
  {
    slug: "link-accounts-nudge",
    title: "Привяжите GitHub и Telegram",
    description:
      "Пользователям БЕЗ привязки GitHub: зовёт привязать GitHub и Telegram в настройках " +
      "интеграций. Только уведомление на сайте (без письма), отписка работает штатно. " +
      "Повторный запуск пропускает уже уведомлённых.",
    Icon: Icons.link,
    testSuccessMessage: "Уведомление отправлено в ваш инбокс — проверьте колокольчик",
  },
];

export function AdminCampaignsPage() {
  return (
    <div className="space-y-6 max-w-[900px] mx-auto">
      <Card className="relative overflow-hidden">
        <div className="absolute top-0 inset-x-0 h-0.5 bg-gradient-primary" />
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Icons.send size={20} />
            Рассылки
          </CardTitle>
          <CardDescription className="pt-1">
            Запуск платформенных кампаний уведомлений. Доставка идёт через стандартные
            каналы; настройки и отписки пользователей учитываются, если карточка кампании
            не говорит иного.
          </CardDescription>
        </CardHeader>
      </Card>

      {CAMPAIGNS.map((campaign) => (
        <CampaignCard key={campaign.slug} campaign={campaign} />
      ))}
    </div>
  );
}

function CampaignCard({ campaign }: { campaign: CampaignConfig }) {
  const { data: recipients, isLoading: countLoading } = useRecipientCount(campaign.slug);
  const sendTest = useSendTestCampaign(campaign.slug, campaign.testSuccessMessage);
  const runCampaign = useRunCampaign(campaign.slug);
  const [confirmOpen, setConfirmOpen] = useState(false);

  const recipientCount = recipients?.count ?? 0;

  const handleRun = async (event: React.MouseEvent) => {
    // Prevent AlertDialog from auto-closing so the spinner stays visible while pending.
    event.preventDefault();
    await runCampaign.mutateAsync();
    setConfirmOpen(false);
  };

  return (
    <>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <campaign.Icon size={18} className="text-primary" />
            {campaign.title}
          </CardTitle>
          <CardDescription className="pt-1">{campaign.description}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="flex items-center gap-2 text-sm">
            <Icons.users size={16} className="text-muted-foreground" />
            <span className="text-muted-foreground">Получателей:</span>
            {countLoading ? (
              <Icons.loading size={16} className="animate-spin text-muted-foreground" />
            ) : (
              <span className="font-semibold tabular-nums">~{recipientCount}</span>
            )}
          </div>

          <div className="flex flex-col gap-3 sm:flex-row">
            <Button
              variant="outline"
              onClick={() => sendTest.mutate()}
              disabled={sendTest.isPending}
            >
              {sendTest.isPending ? (
                <Icons.loading size={16} className="animate-spin" />
              ) : (
                <Icons.mail size={16} />
              )}
              Отправить тест себе
            </Button>

            <Button onClick={() => setConfirmOpen(true)} disabled={runCampaign.isPending}>
              {runCampaign.isPending ? (
                <Icons.loading size={16} className="animate-spin" />
              ) : (
                <Icons.send size={16} />
              )}
              Запустить рассылку
            </Button>
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Запустить рассылку?</AlertDialogTitle>
            <AlertDialogDescription>
              «{campaign.title}»: отправить уведомление всем {recipientCount} получателям?
              Действие необратимо.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={runCampaign.isPending}>Отмена</AlertDialogCancel>
            <AlertDialogAction onClick={handleRun} disabled={runCampaign.isPending}>
              {runCampaign.isPending && (
                <Icons.loading size={16} className="mr-2 animate-spin" />
              )}
              Запустить
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
