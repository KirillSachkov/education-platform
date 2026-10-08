"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { formatPriceFromCents } from "@/entities/access-plan";
import { resolveTrainerProOfferCard, trainerProOfferQueryOptions } from "@/entities/trainer-pro";
import { BuyTrainerProButton } from "@/features/buy-trainer-pro";
import { routes } from "@/shared/config/routes";
import { Icons, type IconComponent } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/ui/kit/dialog";

/**
 * Пейволл «Тренажёр Pro» (#658) — красивый диалог ВМЕСТО тоста-ошибки «недостаточно
 * прав», когда не-подписчик пытается запустить PRO-only действие (мок-интервью).
 * Резолвит оффер через выделенный trainer-API (`GET /access/trainer-pro/offer/`) и
 * `BuyTrainerProButton` (он сам ведёт анонима на логин и инициирует оплату server-side).
 * Нет опубликованного оффера — «скоро появится» + ссылка на лендинг.
 */
const PERKS: ReadonlyArray<{ icon: IconComponent; text: string }> = [
  { icon: Icons.briefcase, text: "Полные мок-интервью с разбором в конце" },
  { icon: Icons.mic, text: "Голосовые ответы — ИИ распознаёт и оценивает" },
  { icon: Icons.message, text: "Развёрнутые ответы с AI-разбором" },
  { icon: Icons.library, text: "Все банки вопросов, без дневных лимитов" },
];

export function TrainerProPaywallDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const { data: offers } = useQuery({ ...trainerProOfferQueryOptions(), enabled: open });

  const offer = resolveTrainerProOfferCard(offers);
  const priceLabel = offer
    ? `${formatPriceFromCents(offer.priceCents, offer.currency)}${offer.monthly ? "/мес" : ""}`
    : null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <span className="inline-flex size-10 items-center justify-center rounded-xl bg-violet-500/15 text-violet-500 dark:text-violet-300">
            <Icons.energy className="size-5" />
          </span>
          <DialogTitle className="mt-3">Мок-интервью — в подписке Тренажёр Pro</DialogTitle>
          <DialogDescription>
            Полная симуляция собеседования с разбором ответов открыта по подписке. Лимиты —
            как у всех Pro.
          </DialogDescription>
        </DialogHeader>

        <ul className="space-y-2.5 text-sm">
          {PERKS.map(({ icon: Icon, text }) => (
            <li key={text} className="flex items-start gap-2.5">
              <Icon className="mt-0.5 size-4 shrink-0 text-violet-500 dark:text-violet-300" />
              <span className="text-foreground/85">{text}</span>
            </li>
          ))}
        </ul>

        <DialogFooter className="flex-col gap-2 sm:flex-col sm:space-x-0">
          {offer && priceLabel ? (
            <BuyTrainerProButton
              planId={offer.planId}
              priceCents={offer.priceCents}
              currency={offer.currency}
              size="lg"
              className="w-full bg-violet-600 text-white hover:bg-violet-600/90"
              label={`Оформить Тренажёр Pro — ${priceLabel}`}
            />
          ) : (
            <p className="text-center text-sm text-muted-foreground">Подписка скоро появится.</p>
          )}
          <Button asChild variant="ghost" className="w-full">
            <Link href={routes.trainerPro} onClick={() => onOpenChange(false)}>
              Подробнее о подписке
            </Link>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
