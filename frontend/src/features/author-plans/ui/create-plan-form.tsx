"use client";

import type { CourseSummaryDto } from "@/entities/course";
import { coursesQueryOptions } from "@/entities/course";
import {
  ALL_PLAN_CAPABILITIES,
  PLAN_CAPABILITY_DESCRIPTIONS,
  PLAN_CAPABILITY_LABELS,
  PLAN_PRESETS,
  type PlanCapability,
  type PlanTier,
} from "@/entities/access-plan";
import type { PlanOfferType } from "@/shared/config/offer-type";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { OfferTypeField } from "./offer-type-field";
import { useInfiniteQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { useCreatePlan } from "../model/use-author-plans";

const TIER_ICONS: Record<PlanTier, keyof typeof Icons> = {
  FULL_ALL: "crown",
  LEARN_ALL: "library",
  COURSE: "grid",
  // Legacy/deprecated tiers — оставлены для defensive рендера, но Create-форма
  // их не предлагает (PLAN_PRESETS не содержит FREE/LEARN_ALL/SUBSCRIPTION).
  FREE: "gift",
  SUBSCRIPTION: "settings",
};

export function CreatePlanForm() {
  const router = useRouter();
  const createPlan = useCreatePlan();

  const [tier, setTier] = useState<PlanTier>("FULL_ALL");
  const [slug, setSlug] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [shortDescription, setShortDescription] = useState("");
  const [longDescription, setLongDescription] = useState("");
  const [features, setFeatures] = useState<string[]>([]);
  const [featureDraft, setFeatureDraft] = useState("");
  // Хранится как рубли (как вводит автор). Конвертируется в копейки (×100) на submit'е.
  const [priceRubles, setPriceRubles] = useState("");
  const [isHighlighted, setIsHighlighted] = useState(false);
  const [capabilities, setCapabilities] = useState<Set<PlanCapability>>(
    () => new Set(["VIEW_MATERIALS", "SUBMIT_ISSUES"]),
  );
  const [selectedCourseIds, setSelectedCourseIds] = useState<string[]>([]);
  // Offer-type для COURSE-tier (default COURSE). Для FULL_ALL форсится FULL_ACCESS на
  // submit'е — этот state тогда не используется.
  const [courseOfferType, setCourseOfferType] = useState<PlanOfferType>("COURSE");

  const courseSelectionInvalid = tier === "COURSE" && selectedCourseIds.length === 0;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!slug.trim() || !displayName.trim()) {
      return;
    }
    if (courseSelectionInvalid) {
      return;
    }
    const rublesParsed = priceRubles.trim() ? Number.parseInt(priceRubles.trim(), 10) : null;
    const result = await createPlan.mutateAsync({
      tier,
      slug: slug.trim(),
      displayName: displayName.trim(),
      shortDescription: shortDescription.trim() || undefined,
      longDescription: longDescription.trim() || undefined,
      features: features.length > 0 ? features : undefined,
      priceCents: rublesParsed && rublesParsed > 0 ? rublesParsed * 100 : null,
      currency: rublesParsed && rublesParsed > 0 ? "RUB" : undefined,
      courseIds: tier === "COURSE" ? selectedCourseIds : [],
      // Capabilities передаём только для COURSE — для остальных tier'ов backend их сам форсит.
      capabilities: tier === "COURSE" ? [...capabilities] : undefined,
      isHighlighted,
      // Offer-type: FULL_ALL форсится FULL_ACCESS; COURSE — выбор автора.
      offerType: tier === "COURSE" ? courseOfferType : "FULL_ACCESS",
    });
    if (result.result) {
      router.push(routes.authorPlanDetail(result.result));
    }
  };

  const toggleCourse = (courseId: string) => {
    setSelectedCourseIds((prev) =>
      prev.includes(courseId) ? prev.filter((id) => id !== courseId) : [...prev, courseId],
    );
  };

  const toggleCapability = (cap: PlanCapability) => {
    setCapabilities((prev) => {
      const next = new Set(prev);
      if (next.has(cap)) next.delete(cap);
      else next.add(cap);
      return next;
    });
  };

  const addFeature = () => {
    const trimmed = featureDraft.trim();
    if (!trimmed) return;
    setFeatures((prev) => [...prev, trimmed]);
    setFeatureDraft("");
  };

  const removeFeature = (idx: number) => {
    setFeatures((prev) => prev.filter((_, i) => i !== idx));
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
        <h1 className="text-2xl font-semibold tracking-tight">Новый план доступа</h1>
        <p className="text-sm text-muted-foreground">
          План определяет, к какому контенту получат доступ ученики через пригласительную ссылку
          или активацию бесплатного плана.
        </p>
      </header>

      <form onSubmit={onSubmit} className="space-y-8">
        <section className="space-y-3">
          <Label className="text-sm font-medium">Тип плана</Label>
          <div className="grid gap-3 sm:grid-cols-1">
            {PLAN_PRESETS.map((spec) => {
              const Icon = Icons[TIER_ICONS[spec.tier]];
              const active = tier === spec.tier;
              return (
                <button
                  key={spec.tier}
                  type="button"
                  onClick={() => setTier(spec.tier)}
                  className={`flex items-start gap-3 rounded-lg border p-4 text-left transition ${
                    active
                      ? "border-primary bg-primary/5 ring-1 ring-primary/40"
                      : "border-input hover:border-muted-foreground/50 hover:bg-muted/40"
                  }`}
                >
                  <div
                    className={`flex size-9 shrink-0 items-center justify-center rounded-md ${
                      active ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground"
                    }`}
                  >
                    <Icon className="size-4" />
                  </div>
                  <div className="space-y-1">
                    <div className="text-sm font-medium leading-tight">{spec.label}</div>
                    <div className="text-xs leading-snug text-muted-foreground">{spec.hint}</div>
                    {spec.isSingleton ? (
                      <div className="text-[11px] text-amber-600 dark:text-amber-500">
                        Один такой опубликованный план на платформу
                      </div>
                    ) : null}
                  </div>
                </button>
              );
            })}
          </div>
        </section>

        <OfferTypeField
          tier={tier}
          value={courseOfferType}
          onChange={setCourseOfferType}
        />

        {tier === "COURSE" ? (
          <CoursePicker
            selectedIds={selectedCourseIds}
            onToggle={toggleCourse}
            invalid={courseSelectionInvalid}
          />
        ) : null}

        <section className="space-y-4">
          <h2 className="text-base font-semibold">Описание</h2>

          <div className="space-y-2">
            <Label htmlFor="plan-display-name">Название</Label>
            <Input
              id="plan-display-name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="Например: Полный доступ"
              maxLength={200}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="plan-slug">Адрес ссылки (на латинице)</Label>
            <Input
              id="plan-slug"
              value={slug}
              onChange={(e) => setSlug(e.target.value)}
              placeholder="full-access"
              maxLength={100}
              pattern="[a-z0-9-]+"
              required
            />
            <p className="text-xs text-muted-foreground">
              Только латиница, цифры и дефис. Используется в URL: <code>/pricing/{slug || "..."}</code>
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="plan-short-desc">Короткое описание (необязательно)</Label>
            <Textarea
              id="plan-short-desc"
              value={shortDescription}
              onChange={(e) => setShortDescription(e.target.value)}
              rows={3}
              maxLength={500}
              placeholder="1–2 предложения о том, что входит в план"
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="plan-long-desc">Подробное описание (необязательно)</Label>
            <Textarea
              id="plan-long-desc"
              value={longDescription}
              onChange={(e) => setLongDescription(e.target.value)}
              rows={6}
              maxLength={5000}
              placeholder="Расскажите подробно: для кого этот план, что в него входит, чем отличается от других. Поддерживается обычный текст с переносами строк."
            />
            <p className="text-xs text-muted-foreground">
              Отображается на странице /pricing под выбранным планом.
            </p>
          </div>
        </section>

        <section className="space-y-2">
          <Label htmlFor="plan-price">Цена (рубли)</Label>
          <Input
            id="plan-price"
            type="number"
            min={0}
            step={100}
            value={priceRubles}
            onChange={(e) => setPriceRubles(e.target.value)}
            placeholder="например, 99000"
          />
          <p className="text-xs text-muted-foreground">
            Без копеек. Оставьте пустым, если цена «по запросу».
          </p>
        </section>

        {tier === "COURSE" ? (
          <section className="space-y-3">
            <div className="space-y-1">
              <Label className="text-sm font-medium">Что входит в план (capabilities)</Label>
              <p className="text-xs text-muted-foreground">
                Бэкенд проверяет capabilities при действиях (отправка задания, доступ в чат) — снятая
                галочка скроет действие у ученика.
              </p>
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
              {ALL_PLAN_CAPABILITIES.map((cap) => {
                const checked = capabilities.has(cap);
                return (
                  <button
                    key={cap}
                    type="button"
                    onClick={() => toggleCapability(cap)}
                    className={`flex items-start gap-2.5 rounded-md border px-3 py-2.5 text-left text-sm transition ${
                      checked
                        ? "border-primary/50 bg-primary/5"
                        : "border-input hover:bg-muted/40"
                    }`}
                  >
                    <span
                      className={`mt-0.5 flex size-5 shrink-0 items-center justify-center rounded border ${
                        checked
                          ? "border-primary bg-primary text-primary-foreground"
                          : "border-input bg-background"
                      }`}
                    >
                      {checked ? <Icons.check className="size-3.5" /> : null}
                    </span>
                    <span className="space-y-0.5">
                      <span className={`block ${checked ? "" : "text-muted-foreground"}`}>
                        {PLAN_CAPABILITY_LABELS[cap]}
                      </span>
                      <span className="block text-xs text-muted-foreground">
                        {PLAN_CAPABILITY_DESCRIPTIONS[cap]}
                      </span>
                    </span>
                  </button>
                );
              })}
            </div>
          </section>
        ) : null}

        <section className="space-y-3">
          <div className="space-y-1">
            <Label className="text-sm font-medium">Дополнительные пункты</Label>
            <p className="text-xs text-muted-foreground">
              Опциональный список фич сверх стандартных capabilities (например, «Чат поддержки 24/7»,
              «Сертификат об окончании»). Отображаются в карточке плана отдельным блоком.
            </p>
          </div>
          <div className="flex gap-2">
            <Input
              value={featureDraft}
              onChange={(e) => setFeatureDraft(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  addFeature();
                }
              }}
              placeholder="например, Сертификат об окончании"
              maxLength={120}
            />
            <Button
              type="button"
              variant="outline"
              onClick={addFeature}
              disabled={!featureDraft.trim()}
            >
              Добавить
            </Button>
          </div>
          {features.length > 0 ? (
            <ul className="space-y-1.5">
              {features.map((feature, idx) => (
                <li
                  key={`${idx}-${feature}`}
                  className="flex items-center gap-2 rounded-md border bg-muted/20 px-3 py-2 text-sm"
                >
                  <Icons.completed className="size-4 shrink-0 text-emerald-500" />
                  <span className="flex-1">{feature}</span>
                  <button
                    type="button"
                    onClick={() => removeFeature(idx)}
                    className="text-xs text-muted-foreground hover:text-destructive"
                  >
                    Удалить
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </section>

        <section className="space-y-3">
          <div className="flex items-start gap-3 rounded-md border border-input p-3">
            <button
              type="button"
              onClick={() => setIsHighlighted((v) => !v)}
              className={`mt-0.5 flex size-5 shrink-0 items-center justify-center rounded border transition ${
                isHighlighted
                  ? "border-primary bg-primary text-primary-foreground"
                  : "border-input bg-background"
              }`}
            >
              {isHighlighted ? <Icons.check className="size-3.5" /> : null}
            </button>
            <label className="flex-1 space-y-0.5">
              <span className="text-sm font-medium">Выделить как «Хит»</span>
              <p className="text-xs text-muted-foreground">
                Бейдж «Хит» на pricing-странице и визуальное выделение карточки. Стоит ставить
                ровно одному flagship-плану.
              </p>
            </label>
          </div>
        </section>

        <div className="flex items-center justify-end gap-2 border-t pt-6">
          <Button type="button" variant="outline" asChild>
            <Link href={routes.authorPlans}>Отмена</Link>
          </Button>
          <Button
            type="submit"
            disabled={
              createPlan.isPending ||
              !displayName.trim() ||
              !slug.trim() ||
              courseSelectionInvalid
            }
          >
            {createPlan.isPending ? "Создаём..." : "Создать план"}
          </Button>
        </div>
      </form>
    </div>
  );
}

/**
 * Мульти-селект курсов для COURSE-плана (#404 bundle plans). Тоггл добавляет/
 * убирает курс из набора; план может содержать несколько курсов.
 */
function CoursePicker({
  selectedIds,
  onToggle,
  invalid,
}: {
  selectedIds: string[];
  onToggle: (id: string) => void;
  invalid: boolean;
}) {
  const coursesQuery = useInfiniteQuery(
    coursesQueryOptions.getMyCoursesInfiniteOptions({ limit: 50 }),
  );

  const items: CourseSummaryDto[] = coursesQuery.data?.items ?? [];

  return (
    <section className="space-y-3">
      <div className="flex items-baseline justify-between">
        <h2 className="text-base font-semibold">Курсы плана</h2>
        <span className="text-xs text-muted-foreground">
          {selectedIds.length > 0 ? `выбрано ${selectedIds.length}` : "не выбрано"}
          {coursesQuery.data?.totalCount ? ` из ${coursesQuery.data.totalCount}` : ""}
        </span>
      </div>
      <p className="text-xs text-muted-foreground">
        Можно добавить несколько курсов — план откроет доступ ко всем выбранным.
      </p>

      {coursesQuery.isLoading ? (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-14 rounded-md bg-muted/40 animate-pulse" />
          ))}
        </div>
      ) : items.length === 0 ? (
        <Card className="p-6 text-center text-sm text-muted-foreground">
          У вас пока нет курсов. Создайте курс, чтобы добавить его в план.
        </Card>
      ) : (
        <div
          className={cn("divide-y rounded-lg border", invalid && "border-destructive/60")}
        >
          {items.map((course) => {
            const checked = selectedIds.includes(course.id);
            return (
              <button
                key={course.id}
                type="button"
                onClick={() => onToggle(course.id)}
                className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-muted/40"
              >
                <span
                  className={`flex size-5 shrink-0 items-center justify-center rounded border transition ${
                    checked
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-input bg-background"
                  }`}
                >
                  {checked ? <Icons.check className="size-3.5" /> : null}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="truncate text-sm font-medium">{course.title}</span>
                    <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-mono text-muted-foreground">
                      {course.status}
                    </span>
                  </div>
                  <div className="text-xs text-muted-foreground">/{course.slug}</div>
                </div>
              </button>
            );
          })}
        </div>
      )}

      {invalid ? (
        <p className="text-xs text-destructive">Выберите хотя бы один курс для плана.</p>
      ) : null}

      {coursesQuery.hasNextPage ? (
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={coursesQuery.isFetchingNextPage}
          onClick={() => coursesQuery.fetchNextPage()}
        >
          {coursesQuery.isFetchingNextPage ? "Загрузка..." : "Показать ещё"}
        </Button>
      ) : null}
    </section>
  );
}
