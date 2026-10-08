"use client";

import { routes } from "@/shared/config/routes";
import { Button } from "@/shared/ui/kit/button";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useCreatePlan } from "../model/use-author-plans";

/**
 * CreateTrialPlanForm (#595) — упрощённая форма «Пробного доступа»: автор задаёт
 * только название, slug, цену и описания. Tier и срок НЕ выбираются — сервер
 * форсит FULL_ALL + FULL_ACCESS и сам ставит срок (месяц) из конфига при
 * `isTrial:true`. Trial — singleton: повторное создание → 409 plan.trial.duplicate.
 */
export function CreateTrialPlanForm() {
  const router = useRouter();
  const createPlan = useCreatePlan();

  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [shortDescription, setShortDescription] = useState("");
  const [longDescription, setLongDescription] = useState("");
  // Хранится как рубли (как вводит автор). Конвертируется в копейки (×100) на submit'е.
  const [priceRubles, setPriceRubles] = useState("");

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!slug.trim() || !displayName.trim()) {
      return;
    }
    const rublesParsed = priceRubles.trim() ? Number.parseInt(priceRubles.trim(), 10) : null;
    const result = await createPlan.mutateAsync({
      // Сервер форсит FULL_ALL/FULL_ACCESS и срок — но tier обязателен в контракте.
      tier: "FULL_ALL",
      isTrial: true,
      slug: slug.trim(),
      displayName: displayName.trim(),
      shortDescription: shortDescription.trim() || undefined,
      longDescription: longDescription.trim() || undefined,
      priceCents: rublesParsed && rublesParsed > 0 ? rublesParsed * 100 : null,
      currency: rublesParsed && rublesParsed > 0 ? "RUB" : undefined,
      offerType: "FULL_ACCESS",
    });
    if (result.result) {
      router.push(routes.authorPlanDetail(result.result));
    }
  };

  return (
    <div className="mx-auto mt-8 max-w-3xl space-y-6 px-4 pb-16">
      <Link
        href={routes.authorPlans}
        className="inline-flex items-center gap-2 text-sm text-muted-foreground transition hover:text-foreground"
      >
        <Icons.back className="size-4" />
        К списку планов
      </Link>

      <header className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Пробный доступ</h1>
        <p className="text-sm text-muted-foreground">
          Полный доступ к обучению .NET Fullstack на месяц. Решит продолжить — доплатит
          разницу до полного доступа навсегда, уплаченное пойдёт в зачёт. Такой план может
          быть только один.
        </p>
      </header>

      <form onSubmit={onSubmit} className="space-y-8">
        <section className="space-y-4">
          <h2 className="text-base font-semibold">Описание</h2>

          <div className="space-y-2">
            <Label htmlFor="trial-display-name">Название</Label>
            <Input
              id="trial-display-name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Например: Полный доступ на месяц"
              maxLength={200}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="trial-slug">Адрес ссылки (на латинице)</Label>
            <Input
              id="trial-slug"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="full-access-month"
              maxLength={100}
              pattern="[a-z0-9-]+"
              required
            />
            <p className="text-xs text-muted-foreground">
              Только латиница, цифры и дефис. Используется в URL: <code>/pricing/{slug || "..."}</code>
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="trial-short-desc">Короткое описание (необязательно)</Label>
            <Textarea
              id="trial-short-desc"
              value={shortDescription}
              onChange={(e) => setShortDescription(e.target.value)}
              rows={3}
              maxLength={500}
              placeholder="1–2 предложения о пробном доступе"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="trial-long-desc">Подробное описание (необязательно)</Label>
            <Textarea
              id="trial-long-desc"
              value={longDescription}
              onChange={(e) => setLongDescription(e.target.value)}
              rows={6}
              maxLength={5000}
              placeholder="Расскажите подробно: что входит в месяц доступа, как работает доплата до полного доступа."
            />
            <p className="text-xs text-muted-foreground">
              Отображается на странице /pricing под сегментом «На месяц».
            </p>
          </div>
        </section>

        <section className="space-y-2">
          <Label htmlFor="trial-price">Цена (рубли)</Label>
          <Input
            id="trial-price"
            type="number"
            min={0}
            step={100}
            value={priceRubles}
            onChange={(e) => setPriceRubles(e.target.value)}
            placeholder="например, 15000"
          />
          <p className="text-xs text-muted-foreground">
            Без копеек. Цена за месяц полного доступа.
          </p>
        </section>

        <section className="flex items-start gap-2.5 rounded-xl border border-primary/20 bg-primary/[0.04] px-4 py-3">
          <Icons.clock className="mt-0.5 size-4 shrink-0 text-primary" />
          <p className="text-xs leading-relaxed text-muted-foreground">
            Срок (месяц) и тип доступа задаёт система — выбирать их не нужно. После создания можно
            будет править цену и описание.
          </p>
        </section>

        <div className="flex items-center justify-end gap-2 border-t pt-6">
          <Button type="button" variant="outline" asChild>
            <Link href={routes.authorPlans}>Отмена</Link>
          </Button>
          <Button
            type="submit"
            disabled={createPlan.isPending || !displayName.trim() || !slug.trim()}
          >
            {createPlan.isPending ? "Создаём..." : "Создать пробный доступ"}
          </Button>
        </div>
      </form>
    </div>
  );
}
