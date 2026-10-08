"use client";

import { formatPriceFromCents } from "@/entities/access-plan";
import {
  trainerProAdminOfferQueryOptions,
  type TrainerProOfferAdminDto,
} from "@/entities/trainer-pro";
import { getErrorMessage } from "@/shared/api";
import { StatusBadge } from "@/shared/ui/components";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Switch } from "@/shared/ui/kit/switch";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useCreateTrainerSubscription } from "../model/use-create-trainer-subscription";
import { useUpdateTrainerSubscription } from "../model/use-update-trainer-subscription";

const DEFAULT_DISPLAY_NAME = "Тренажёр Pro";
const DEFAULT_INTERVAL_DAYS = 30;
const DEFAULT_SLUG = "trainer-pro";

/** Подсказка slug из названия: латиница → kebab-case, иначе дефолтный slug. */
function suggestSlug(title: string): string {
  const base = title
    .toLowerCase()
    .normalize("NFKD")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 48);
  return base || DEFAULT_SLUG;
}

/** Парсит рубли из инпута в копейки (целые рубли × 100); пусто/0/нечисло → null. */
function rublesToCents(raw: string): number | null {
  const parsed = raw.trim() ? Number.parseInt(raw.trim(), 10) : null;
  return parsed && parsed > 0 ? parsed * 100 : null;
}

/** Textarea «строка = преимущество» → массив без пустых строк. */
function parseFeatures(raw: string): string[] {
  return raw
    .split("\n")
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/**
 * Вкладка «Подписка» хаба тренажёра (#674) — управление оффером «Тренажёр Pro» через
 * выделенный trainer-API (`/access/admin/trainer-pro/offer`), отдельно от платформенных
 * планов. Есть офферы — карточки редактирования (цена / название / преимущества /
 * покупаемость); нет — форма создания. Гейт `plans.manage` обеспечивает сама страница.
 */
export function TrainerSubscriptionManager() {
  const offersQuery = useQuery(trainerProAdminOfferQueryOptions());

  if (offersQuery.isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-9 w-64 rounded-lg" />
        <Skeleton className="h-52 w-full rounded-xl" />
      </div>
    );
  }

  if (offersQuery.isError) {
    return (
      <EmptyState
        variant="card"
        icon={Icons.warning}
        title="Не удалось загрузить подписку"
        description={getErrorMessage(offersQuery.error, "Повторите попытку позже")}
        action={
          <Button variant="outline" onClick={() => offersQuery.refetch()}>
            <Icons.refresh className="size-4" />
            Попробовать снова
          </Button>
        }
      />
    );
  }

  const offers = offersQuery.data;

  return (
    <div className="space-y-5">
      <IntroNote />
      {offers.length === 0 ? (
        <CreateSubscription />
      ) : (
        offers.map((offer) => <EditSubscription key={offer.id} offer={offer} />)
      )}
    </div>
  );
}

function IntroNote() {
  return (
    <Card className="border-violet-500/30 bg-violet-500/5">
      <CardContent className="flex items-start gap-3 p-4 text-sm">
        <Icons.crown className="mt-0.5 size-4 shrink-0 text-violet-500" />
        <p className="text-muted-foreground">
          Подписка <span className="font-medium text-foreground">Тренажёр Pro</span> открывает
          расширенный тренажёр собеседований: открытые ответы, голосовые мок-интервью и работу без
          лимитов. Точные ограничения PRO настраиваются в конфигурации сервиса.
        </p>
      </CardContent>
    </Card>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function EditSubscription({ offer }: { offer: TrainerProOfferAdminDto }) {
  const update = useUpdateTrainerSubscription(offer.id);
  const [displayName, setDisplayName] = useState(offer.displayName);
  const initialRubles = offer.priceCents != null ? String(Math.floor(offer.priceCents / 100)) : "";
  const [priceRubles, setPriceRubles] = useState(initialRubles);
  const initialFeatures = offer.features.join("\n");
  const [features, setFeatures] = useState(initialFeatures);
  const [isActive, setIsActive] = useState(offer.isPublic);

  const effectivePrice = offer.effectivePriceCents ?? offer.priceCents;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim()) return;
    const priceCents = rublesToCents(priceRubles);
    await update.mutateAsync({
      displayName: displayName.trim(),
      priceCents,
      currency: priceCents != null ? "RUB" : undefined,
      features: parseFeatures(features),
      isActive,
    });
  };

  const dirty =
    displayName.trim() !== offer.displayName ||
    priceRubles.trim() !== initialRubles ||
    features !== initialFeatures ||
    isActive !== offer.isPublic;

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-2">
        <CardTitle className="text-base">{offer.displayName}</CardTitle>
        <StatusBadge status={offer.isPublic ? "PUBLISHED" : "DRAFT"} />
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
          <Stat
            label="Цена"
            value={
              effectivePrice != null ? formatPriceFromCents(effectivePrice, offer.currency) : "—"
            }
          />
          <Stat
            label="Период"
            value={offer.recurringIntervalDays ? `${offer.recurringIntervalDays} дн.` : "—"}
          />
          <Stat label="Slug" value={`/${offer.slug}`} mono />
        </div>

        <form onSubmit={onSubmit} className="space-y-4 border-t pt-5">
          <div className="space-y-2">
            <Label htmlFor={`sub-name-${offer.id}`}>Название</Label>
            <Input
              id={`sub-name-${offer.id}`}
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              maxLength={200}
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor={`sub-price-${offer.id}`}>Цена (рубли, за период)</Label>
            <Input
              id={`sub-price-${offer.id}`}
              type="number"
              min={0}
              step={100}
              value={priceRubles}
              onChange={(e) => setPriceRubles(e.target.value)}
              placeholder="например, 990"
            />
            <p className="text-xs text-muted-foreground">
              Без копеек. Списывается каждые {offer.recurringIntervalDays ?? DEFAULT_INTERVAL_DAYS}{" "}
              дней.
            </p>
          </div>
          <div className="space-y-2">
            <Label htmlFor={`sub-features-${offer.id}`}>Преимущества (по одному на строку)</Label>
            <Textarea
              id={`sub-features-${offer.id}`}
              value={features}
              onChange={(e) => setFeatures(e.target.value)}
              rows={4}
              placeholder={"Голосовые ответы\nМок-интервью\nВсе банки вопросов"}
            />
          </div>

          <div className="flex items-center justify-between gap-3 rounded-lg border bg-card p-3">
            <div className="space-y-0.5">
              <Label htmlFor={`sub-active-${offer.id}`}>Покупаемость</Label>
              <p className="text-xs text-muted-foreground">
                {isActive ? "Опубликован — доступен для оплаты" : "Скрыт — недоступен для оплаты"}
              </p>
            </div>
            <Switch
              id={`sub-active-${offer.id}`}
              checked={isActive}
              onCheckedChange={setIsActive}
              aria-label="Покупаемость подписки"
            />
          </div>

          <div className="flex justify-end border-t pt-4">
            <Button type="submit" disabled={update.isPending || !displayName.trim() || !dirty}>
              {update.isPending ? "Сохраняем..." : "Сохранить"}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

function Stat({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border bg-card p-3">
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div
        className={`mt-1 text-lg font-semibold ${mono ? "truncate font-mono text-sm" : "tabular-nums"}`}
      >
        {value}
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────

function CreateSubscription() {
  const create = useCreateTrainerSubscription();
  const [displayName, setDisplayName] = useState(DEFAULT_DISPLAY_NAME);
  const [slug, setSlug] = useState(DEFAULT_SLUG);
  const [slugTouched, setSlugTouched] = useState(false);
  const [priceRubles, setPriceRubles] = useState("");
  const [intervalDays, setIntervalDays] = useState(String(DEFAULT_INTERVAL_DAYS));
  const [shortDescription, setShortDescription] = useState("");

  const parsedInterval = Number.parseInt(intervalDays.trim(), 10);
  const intervalInvalid = !Number.isFinite(parsedInterval) || parsedInterval <= 0;
  const priceCents = rublesToCents(priceRubles);
  const priceInvalid = priceCents == null;

  const onDisplayNameChange = (value: string) => {
    setDisplayName(value);
    if (!slugTouched) setSlug(suggestSlug(value));
  };

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim() || !slug.trim() || intervalInvalid || priceCents == null) return;
    await create.mutateAsync({
      slug: slug.trim(),
      displayName: displayName.trim(),
      priceCents,
      recurringIntervalDays: parsedInterval,
      shortDescription: shortDescription.trim() || undefined,
      currency: "RUB",
      isActive: true,
    });
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Создать подписку</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="mb-5 text-sm text-muted-foreground">
          Подписки «Тренажёр Pro» пока нет. Создайте оффер — он сразу станет покупаемым в тренажёре.
        </p>
        <form onSubmit={onSubmit} className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="new-sub-name">Название</Label>
            <Input
              id="new-sub-name"
              value={displayName}
              onChange={(e) => onDisplayNameChange(e.target.value)}
              maxLength={200}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="new-sub-slug">Адрес ссылки (на латинице)</Label>
            <Input
              id="new-sub-slug"
              value={slug}
              onChange={(e) => {
                setSlug(e.target.value);
                setSlugTouched(true);
              }}
              maxLength={100}
              pattern="[a-z0-9-]+"
              required
            />
            <p className="text-xs text-muted-foreground">Только латиница, цифры и дефис.</p>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="new-sub-price">Цена (рубли)</Label>
              <Input
                id="new-sub-price"
                type="number"
                min={0}
                step={100}
                value={priceRubles}
                onChange={(e) => setPriceRubles(e.target.value)}
                placeholder="например, 990"
                required
                aria-invalid={priceInvalid}
              />
              {priceInvalid ? (
                <p className="text-xs text-destructive">Укажите положительную цену в рублях.</p>
              ) : null}
            </div>
            <div className="space-y-2">
              <Label htmlFor="new-sub-interval">Период списания (дни)</Label>
              <Input
                id="new-sub-interval"
                type="number"
                min={1}
                step={1}
                value={intervalDays}
                onChange={(e) => setIntervalDays(e.target.value)}
                required
                aria-invalid={intervalInvalid}
              />
              {intervalInvalid ? (
                <p className="text-xs text-destructive">Укажите положительное число дней.</p>
              ) : null}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="new-sub-desc">Короткое описание (необязательно)</Label>
            <Textarea
              id="new-sub-desc"
              value={shortDescription}
              onChange={(e) => setShortDescription(e.target.value)}
              rows={3}
              maxLength={500}
              placeholder="1–2 предложения о том, что даёт Pro"
            />
          </div>

          <div className="flex justify-end border-t pt-4">
            <Button
              type="submit"
              disabled={
                create.isPending ||
                !displayName.trim() ||
                !slug.trim() ||
                intervalInvalid ||
                priceInvalid
              }
            >
              {create.isPending ? "Создаём..." : "Создать подписку"}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
