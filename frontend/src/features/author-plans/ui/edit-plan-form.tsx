"use client";

import {
  ALL_PLAN_CAPABILITIES,
  discountedCents,
  formatPriceFromCents,
  formatPromotionEndsHint,
  myPlansQueryOptions,
  PLAN_CAPABILITY_DESCRIPTIONS,
  PLAN_CAPABILITY_LABELS,
  type PlanCapability,
  type PlanDto,
} from "@/entities/access-plan";
import type { CourseSummaryDto } from "@/entities/course";
import { coursesQueryOptions } from "@/entities/course";
import { githubAppApi, githubInstallationStatusQueryOptions } from "@/entities/plan-onboarding";
import { getErrorMessage } from "@/shared/api";
import type { PlanOfferType } from "@/shared/config/offer-type";
import { routes } from "@/shared/config/routes";
import { cn } from "@/shared/lib/css";
import { GitHubOrgInput } from "@/shared/ui/components";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { Icons } from "@/shared/ui/icons";
import { Input } from "@/shared/ui/kit/input";
import { Label } from "@/shared/ui/kit/label";
import { Textarea } from "@/shared/ui/kit/textarea";
import { useInfiniteQuery, useMutation, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { type ReactNode, useState } from "react";
import { toast } from "sonner";
import { useClearPromotion, useSetPromotion, useUpdatePlan } from "../model/use-author-plans";
import { OfferTypeField } from "./offer-type-field";

interface EditPlanFormProps {
  planId: string;
  /**
   * Slot для списка Telegram chat-bindings. Композируется на странице, чтобы
   * `features/author-plans` не зависел от `features/admin-plan-telegram`.
   */
  chatBindings?: ReactNode;
}

/**
 * Редактирование существующего плана. Все поля опциональны — backend ждёт
 * <c>UpdatePlanRequest</c> с null-семантикой («не менять»). Мы шлём текущие
 * значения как explicit values; пустые поля шлём как пустые строки/массивы
 * (обновление текстовых полей до пустых) либо null (не менять).
 *
 * Загружает план из <c>myPlansQueryOptions()</c> (там и так живёт список — bytes
 * на одного юзера микроскопические, отдельный fetch по ID не нужен).
 */
export function EditPlanForm({ planId, chatBindings }: EditPlanFormProps) {
  const updatePlan = useUpdatePlan(planId);
  const plansQuery = useQuery(myPlansQueryOptions());
  const plan = plansQuery.data?.find((p) => p.id === planId);

  const [displayName, setDisplayName] = useState("");
  const [shortDescription, setShortDescription] = useState("");
  const [longDescription, setLongDescription] = useState("");
  const [features, setFeatures] = useState<string[]>([]);
  const [featureDraft, setFeatureDraft] = useState("");
  const [priceRubles, setPriceRubles] = useState("");
  const [isHighlighted, setIsHighlighted] = useState(false);
  const [capabilities, setCapabilities] = useState<Set<PlanCapability>>(new Set());
  const [selectedCourseIds, setSelectedCourseIds] = useState<string[]>([]);
  const [offerType, setOfferType] = useState<PlanOfferType>("COURSE");
  const [githubOrgSlug, setGithubOrgSlug] = useState("");
  const [hydrated, setHydrated] = useState(false);

  // Hydrate state из загруженного плана один раз — потом state живёт независимо.
  // setState вызываются прямо в рендере, защищённом флагом hydrated, потому что плана
  // нет до завершения query'и; useEffect вызовет cascading render, что запрещено правилом
  // react-hooks/set-state-in-effect (CLAUDE.md: React Compiler).
  if (plan && !hydrated) {
    setDisplayName(plan.displayName);
    setShortDescription(plan.shortDescription);
    setLongDescription(plan.longDescription);
    setFeatures(plan.features);
    setPriceRubles(plan.priceCents != null ? String(plan.priceCents / 100) : "");
    setIsHighlighted(plan.isHighlighted);
    setCapabilities(new Set(plan.capabilities));
    setSelectedCourseIds(plan.courseIds);
    setOfferType(plan.offerType);
    setGithubOrgSlug(plan.githubOrgSlug ?? "");
    setHydrated(true);
  }

  if (plansQuery.isLoading) {
    return (
      <div className="space-y-4">
        <div className="h-32 rounded-xl bg-muted/40 animate-pulse" />
        <div className="h-64 rounded-xl bg-muted/40 animate-pulse" />
      </div>
    );
  }

  if (!plan) {
    return (
      <div className="text-center">
        <h2 className="text-xl font-semibold">План не найден</h2>
        <Button asChild variant="outline" className="mt-4">
          <Link href={routes.authorPlans}>К списку</Link>
        </Button>
      </div>
    );
  }

  if ((plan.trialDurationDays ?? 0) > 0) {
    return (
      <Card className="border-primary/20 bg-primary/[0.03] p-5">
        <div className="flex items-start gap-3">
          <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Icons.clock className="size-5" />
          </div>
          <div className="min-w-0 space-y-1">
            <h2 className="text-base font-semibold">Системный месячный доступ</h2>
            <p className="text-sm text-muted-foreground">
              Этот план не редактируется как обычный продуктовый план. Он выдаёт полный доступ
              к обучению .NET Fullstack на месяц, а онбординг берётся из бессрочного полного доступа.
            </p>
          </div>
        </div>
      </Card>
    );
  }

  const courseSelectionInvalid = plan?.tier === "COURSE" && selectedCourseIds.length === 0;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim()) return;
    if (courseSelectionInvalid) return;

    const rublesParsed = priceRubles.trim() ? Number.parseInt(priceRubles.trim(), 10) : null;
    await updatePlan.mutateAsync({
      displayName: displayName.trim(),
      shortDescription: shortDescription.trim(),
      longDescription: longDescription.trim(),
      features,
      priceCents: rublesParsed && rublesParsed > 0 ? rublesParsed * 100 : null,
      currency: rublesParsed && rublesParsed > 0 ? "RUB" : "RUB",
      // Курсы плана мутабельны (#404 bundle) — шлём текущий набор для COURSE,
      // пустой для остальных tier'ов.
      courseIds: plan!.tier === "COURSE" ? selectedCourseIds : [],
      // Capabilities автор настраивает только для COURSE — для остальных tier'ов
      // backend форсит дефолты (FREE/LEARN_ALL → VIEW_MATERIALS, FULL_ALL → FULL).
      capabilities: plan!.tier === "COURSE" ? [...capabilities] : undefined,
      isHighlighted,
      // Offer-type мутабелен только для COURSE-tier; для FULL_ALL он forced'ится
      // бэкендом — шлём undefined («не менять»), чтобы не дёргать домен зря.
      offerType: plan!.tier === "COURSE" ? offerType : undefined,
      // Empty string => clear binding, trimmed slug => set new binding.
      githubOrgSlug: githubOrgSlug.trim(),
    });
    // useUpdatePlan invalidate'ит myPlansQueryOptions — UI рефетчит сам.
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
    <div className="space-y-6">
      <p className="text-sm text-muted-foreground">
        Тип плана и адрес ссылки нельзя поменять — создайте новый план, если нужны другие.
      </p>

      <form onSubmit={onSubmit} className="space-y-8">
        <section className="space-y-2">
          <Label className="text-sm font-medium">Тип и адрес</Label>
          <div className="flex flex-wrap items-center gap-2 rounded-md border bg-muted/20 px-3 py-2 text-sm text-muted-foreground">
            <span className="rounded bg-background px-2 py-0.5 font-mono text-xs">
              {plan.tier}
            </span>
            <span className="font-mono text-xs">/{plan.slug}</span>
          </div>
        </section>

        <OfferTypeField tier={plan.tier} value={offerType} onChange={setOfferType} />

        {plan.tier === "COURSE" ? (
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
              maxLength={200}
              required
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="plan-short-desc">Короткое описание</Label>
            <Textarea
              id="plan-short-desc"
              value={shortDescription}
              onChange={(e) => setShortDescription(e.target.value)}
              rows={3}
              maxLength={500}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="plan-long-desc">Подробное описание</Label>
            <Textarea
              id="plan-long-desc"
              value={longDescription}
              onChange={(e) => setLongDescription(e.target.value)}
              rows={6}
              maxLength={5000}
              placeholder="Расскажите подробно: для кого этот план, что в него входит, чем отличается."
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

        {plan.trialDurationDays != null && plan.trialDurationDays > 0 ? (
          <section className="space-y-2">
            <Label>Срок доступа</Label>
            <Input value="Месяц" disabled />
            <p className="text-xs text-muted-foreground">
              Пробный доступ — полный доступ на месяц (срок задаётся системой). Изменить срок
              нельзя; можно править цену и описание.
            </p>
          </section>
        ) : null}

        <PromotionSection plan={plan} />

        <section className="space-y-3">
          <div className="space-y-1">
            <Label className="text-sm font-medium">Что входит в план</Label>
            <p className="text-xs text-muted-foreground">
              {plan.tier === "COURSE"
                ? "Снятая галочка скроет действие у ученика (например, без SUBMIT_ISSUES — нельзя отправить решение)."
                : "Capabilities для этого типа плана зафиксированы — поменять можно только создав COURSE-план."}
            </p>
          </div>
          {plan.tier === "COURSE" ? (
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
          ) : (
            <ul className="space-y-1.5 rounded-md border bg-muted/20 p-3 text-sm">
              {ALL_PLAN_CAPABILITIES.filter((cap) => capabilities.has(cap)).map((cap) => (
                <li key={cap} className="flex items-start gap-2">
                  <Icons.check className="mt-0.5 size-4 shrink-0 text-emerald-500" />
                  <span className="flex-1">
                    <span className="block font-medium">{PLAN_CAPABILITY_LABELS[cap]}</span>
                    <span className="block text-xs text-muted-foreground">
                      {PLAN_CAPABILITY_DESCRIPTIONS[cap]}
                    </span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>

        <section className="space-y-3">
          <div className="space-y-1">
            <Label className="text-sm font-medium">Дополнительные пункты</Label>
            <p className="text-xs text-muted-foreground">
              Опциональный список фич сверх стандартных capabilities (например, «Сертификат»).
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
                Бейдж «Хит» на pricing-странице. Стоит ставить ровно одному flagship-плану.
              </p>
            </label>
          </div>
        </section>

        <section className="space-y-6">
          <h2 className="text-base font-semibold">Интеграции</h2>

          <div className="space-y-2">
            <Label htmlFor="plan-github-org">GitHub организация</Label>
            <GitHubOrgInput
              id="plan-github-org"
              value={githubOrgSlug}
              onChange={setGithubOrgSlug}
              placeholder="например, miracle-generation"
            />
            <p className="text-xs text-muted-foreground">
              Юзеры, состоящие в этой GitHub-организации, при логине через GitHub автоматически
              получат доступ к плану. Один org может быть привязан только к одному активному плану.
            </p>
          </div>

          <GitHubAppConnectButton planId={planId} githubOrgSlug={githubOrgSlug} />

          {chatBindings ? <div className="border-t pt-6">{chatBindings}</div> : null}
        </section>

        <div className="flex items-center justify-end gap-2 border-t pt-6">
          <Button type="button" variant="outline" asChild>
            <Link href={routes.authorPlanDetail(planId)}>Отмена</Link>
          </Button>
          <Button
            type="submit"
            disabled={updatePlan.isPending || !displayName.trim() || courseSelectionInvalid}
          >
            {updatePlan.isPending ? "Сохраняем..." : "Сохранить"}
          </Button>
        </div>
      </form>
    </div>
  );
}

/**
 * Мульти-селект курсов плана (#404 bundle plans). Набор курсов мутабелен через
 * Update — автор может добавлять/убирать курсы у существующего COURSE-плана.
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

/** Конвертирует Date/ISO в значение для `<input type="datetime-local">` (локальное время). */
function toLocalInputValue(value: string | Date): string {
  const d = typeof value === "string" ? new Date(value) : value;
  if (Number.isNaN(d.getTime())) return "";
  const offsetMs = d.getTimezoneOffset() * 60_000;
  return new Date(d.getTime() - offsetMs).toISOString().slice(0, 16);
}

/**
 * Секция настройки акции (промо-скидки) на план. Требует положительную цену.
 * Использует отдельные endpoint'ы PUT/DELETE /promotion/ — не часть UpdatePlanRequest.
 */
function PromotionSection({ plan }: { plan: PlanDto }) {
  const setPromotion = useSetPromotion(plan.id);
  const clearPromotion = useClearPromotion(plan.id);

  const now = new Date();
  const [percent, setPercent] = useState(() =>
    plan.discountPercent != null ? String(plan.discountPercent) : "30",
  );
  const [startsAt, setStartsAt] = useState(() =>
    toLocalInputValue(plan.discountStartsAt ?? now),
  );
  const [endsAt, setEndsAt] = useState(() =>
    toLocalInputValue(plan.discountEndsAt ?? new Date(now.getTime() + 7 * 86_400_000)),
  );

  const hasPrice = plan.priceCents != null && plan.priceCents > 0;
  const hasPromotion = plan.discountPercent != null;

  const percentNum = Number.parseInt(percent, 10);
  const validPercent = Number.isFinite(percentNum) && percentNum >= 1 && percentNum <= 99;
  const previewCents = hasPrice && validPercent ? discountedCents(plan.priceCents!, percentNum) : null;

  const onSet = async () => {
    if (!validPercent) {
      toast.error("Процент скидки должен быть от 1 до 99");
      return;
    }
    if (!startsAt || !endsAt) {
      toast.error("Укажите даты начала и окончания");
      return;
    }
    const startIso = new Date(startsAt).toISOString();
    const endIso = new Date(endsAt).toISOString();
    if (new Date(endIso) <= new Date(startIso)) {
      toast.error("Дата окончания должна быть позже начала");
      return;
    }
    await setPromotion.mutateAsync({
      discountPercent: percentNum,
      startsAt: startIso,
      endsAt: endIso,
    });
  };

  return (
    <section className="space-y-3">
      <div className="space-y-1">
        <h2 className="text-base font-semibold">Акция</h2>
        <p className="text-xs text-muted-foreground">
          Процентная скидка на ограниченный срок. На витрине показывается зачёркнутая старая
          цена, новая цена и бейдж «−N%». При оплате списывается акционная цена.
        </p>
      </div>

      {hasPromotion ? (
        <div
          className={`rounded-md border p-3 text-sm ${
            plan.promotionActive
              ? "border-emerald-500/40 bg-emerald-500/5"
              : "border-amber-500/40 bg-amber-500/5"
          }`}
        >
          {plan.promotionActive ? (
            <span>
              Акция активна: <span className="font-medium">−{plan.discountPercent}%</span>
              {formatPromotionEndsHint(plan.discountEndsAt)
                ? ` · ${formatPromotionEndsHint(plan.discountEndsAt)}`
                : ""}
              {plan.effectivePriceCents != null
                ? ` · цена ${formatPriceFromCents(plan.effectivePriceCents, plan.currency)}`
                : ""}
            </span>
          ) : (
            <span>Акция настроена ({plan.discountPercent}%), но сейчас не активна (вне окна дат).</span>
          )}
        </div>
      ) : null}

      {!hasPrice ? (
        <p className="rounded-md border border-dashed bg-muted/30 p-3 text-xs text-muted-foreground">
          Сначала укажите и сохраните цену плана — акция применяется к ней.
        </p>
      ) : (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="space-y-1.5">
              <Label htmlFor="promo-percent">Скидка, %</Label>
              <Input
                id="promo-percent"
                type="number"
                min={1}
                max={99}
                value={percent}
                onChange={(e) => setPercent(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="promo-starts">Начало</Label>
              <Input
                id="promo-starts"
                type="datetime-local"
                value={startsAt}
                onChange={(e) => setStartsAt(e.target.value)}
              />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="promo-ends">Окончание</Label>
              <Input
                id="promo-ends"
                type="datetime-local"
                value={endsAt}
                onChange={(e) => setEndsAt(e.target.value)}
              />
            </div>
          </div>

          {previewCents != null ? (
            <p className="text-sm text-muted-foreground">
              Было <s>{formatPriceFromCents(plan.priceCents!, plan.currency)}</s> → станет{" "}
              <span className="font-medium text-foreground">
                {formatPriceFromCents(previewCents, plan.currency)}
              </span>{" "}
              (−{percentNum}%)
            </p>
          ) : null}

          <div className="flex items-center gap-2">
            <Button type="button" onClick={onSet} disabled={setPromotion.isPending || !validPercent}>
              {setPromotion.isPending
                ? "Сохраняем..."
                : hasPromotion
                  ? "Обновить акцию"
                  : "Включить акцию"}
            </Button>
            {hasPromotion ? (
              <Button
                type="button"
                variant="outline"
                onClick={() => clearPromotion.mutate()}
                disabled={clearPromotion.isPending}
              >
                {clearPromotion.isPending ? "Убираем..." : "Убрать акцию"}
              </Button>
            ) : null}
          </div>
        </>
      )}
    </section>
  );
}

function GitHubAppConnectButton({
  planId,
  githubOrgSlug,
}: {
  planId: string;
  githubOrgSlug: string;
}) {
  const statusQuery = useQuery(githubInstallationStatusQueryOptions);
  const redirect = useMutation({
    mutationFn: () => githubAppApi.getInstallRedirect({ planId }),
    onSuccess: (data) => {
      const url = data.result?.url;
      if (!url) {
        toast.error("GitHub App не настроен");
        return;
      }
      window.location.href = url;
    },
    onError: (e) => toast.error(getErrorMessage(e, "Не удалось получить ссылку установки")),
  });

  const status = statusQuery.data;
  const installed = status?.isInstalled === true;
  const suspended = installed && status?.isSuspended === true;

  // GitHub-инвайты — opt-in на уровне плана: блок показываем только если у плана
  // задана организация. Без org этот план не использует GitHub, и глобальный статус
  // установки App (он один на автора) к нему не относится — иначе «подключён» течёт
  // в каждый план, даже там, где автор GitHub не хочет.
  if (!githubOrgSlug.trim()) {
    return null;
  }

  return (
    <div
      className={`rounded-md border p-3 text-sm ${
        installed
          ? suspended
            ? "border-amber-500/50 bg-amber-500/5"
            : "border-emerald-500/40 bg-emerald-500/5"
          : "border-dashed bg-muted/30"
      }`}
    >
      <div className="mb-2 flex items-start gap-2">
        <Icons.github className="mt-0.5 h-4 w-4 shrink-0" />
        <div className="min-w-0 flex-1">
          {installed ? (
            <>
              <div className="font-medium">
                {suspended ? "GitHub App приостановлен" : "GitHub App подключён"}
              </div>
              <p className="mt-0.5 text-xs text-muted-foreground">
                {suspended
                  ? "Установка приостановлена на стороне GitHub — авто-инвайты не уйдут. Переустановите, чтобы возобновить."
                  : "Backend сам отправляет приглашение в org, как только ученик попадает в onboarding."}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                Org: <span className="font-mono">{status?.orgLogin ?? "—"}</span>
              </p>
            </>
          ) : (
            <>
              <div className="font-medium">Подключить GitHub App для авто-инвайтов</div>
              <p className="mt-0.5 text-xs text-muted-foreground">
                Без этого автор приглашает учеников в org вручную. После установки App
                backend сам отправляет приглашение, как только ученик попадает в onboarding.
              </p>
            </>
          )}
        </div>
      </div>
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={redirect.isPending || statusQuery.isLoading}
        onClick={() => redirect.mutate()}
      >
        {installed ? "Переустановить" : "Подключить GitHub App"}
      </Button>
    </div>
  );
}
