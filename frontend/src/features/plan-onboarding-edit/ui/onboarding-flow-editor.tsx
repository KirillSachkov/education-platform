"use client";

import { type OnboardingStepDto, onboardingFlowQueryOptions } from "@/entities/plan-onboarding";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/shared/ui/kit/alert-dialog";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/shared/ui/kit/dropdown-menu";
import { Switch } from "@/shared/ui/kit/switch";
import { DragDropProvider } from "@dnd-kit/react";
import { isSortable, useSortable } from "@dnd-kit/react/sortable";
import { useQuery } from "@tanstack/react-query";
import { GripVertical } from "lucide-react";
import { useState } from "react";
import {
  useDeleteStep,
  useReorderStep,
  useResetAllOnboardings,
  useSetEnabled,
  useSetStepIsSkippable,
  useToggleGithubReviewAppStep,
} from "../model/use-onboarding-mutations";
import { Label } from "@/shared/ui/kit/label";
import { MarkdownStepDialog } from "./markdown-step-dialog";

type Props = { planId: string };

export function OnboardingFlowEditor({ planId }: Props) {
  const { data: flow, isPending } = useQuery(onboardingFlowQueryOptions(planId));
  const setEnabled = useSetEnabled(planId);
  const deleteStep = useDeleteStep(planId);
  const reorderStep = useReorderStep(planId);
  const setStepIsSkippable = useSetStepIsSkippable(planId);
  const toggleReviewApp = useToggleGithubReviewAppStep(planId);
  const resetAll = useResetAllOnboardings(planId);

  const [editingStep, setEditingStep] = useState<OnboardingStepDto | null>(null);
  const [isDialogOpen, setIsDialogOpen] = useState(false);

  if (isPending || !flow) {
    return <div className="text-muted-foreground">Загрузка…</div>;
  }

  const hasReviewAppStep = flow.steps.some((s) => s.stepType === "GITHUB_REVIEW_APP");

  const handleAddMarkdown = () => {
    setEditingStep(null);
    setIsDialogOpen(true);
  };

  const handleAddReviewApp = () => {
    if (toggleReviewApp.isPending || hasReviewAppStep) return;
    toggleReviewApp.mutate(true);
  };

  const handleDeleteReviewApp = () => {
    if (toggleReviewApp.isPending) return;
    toggleReviewApp.mutate(false);
  };

  const handleEdit = (step: OnboardingStepDto) => {
    setEditingStep(step);
    setIsDialogOpen(true);
  };

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <p className="text-sm text-muted-foreground">
          После получения plan-grant пользователь увидит этот wizard. Telegram / GitHub /
          уведомления подключаются автоматически по факту привязок плана.
        </p>
      </header>

      <Card className="flex items-center justify-between p-4">
        <div>
          <div className="font-medium">Включить онбординг для плана</div>
          <p className="text-sm text-muted-foreground">
            Без этого toggle новые ученики не увидят wizard.
          </p>
        </div>
        <Switch
          checked={flow.isEnabled}
          onCheckedChange={(checked) => setEnabled.mutate(checked)}
          disabled={setEnabled.isPending}
        />
      </Card>

      <Card className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="font-medium">Перезапустить онбординг всем</div>
          <p className="text-sm text-muted-foreground">
            Все, кто уже получил доступ к плану, пройдут wizard заново. Полезно после
            значимых изменений в шагах.
          </p>
        </div>
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="outline" size="sm" disabled={resetAll.isPending}>
              {resetAll.isPending ? "Перезапускаем…" : "Перезапустить всем"}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Перезапустить онбординг всем?</AlertDialogTitle>
              <AlertDialogDescription>
                Каждый ученик с доступом к этому плану увидит wizard заново — прогресс
                по шагам сбросится на первый. Действие затронет всех текущих
                grant-holder&apos;ов и необратимо.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Отмена</AlertDialogCancel>
              <AlertDialogAction onClick={() => resetAll.mutate()}>
                Перезапустить
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </Card>

      <section className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-medium">Шаги</h2>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button size="sm" disabled={toggleReviewApp.isPending}>
                + Добавить шаг
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={handleAddMarkdown}>Markdown-страница</DropdownMenuItem>
              <DropdownMenuItem
                onSelect={handleAddReviewApp}
                disabled={hasReviewAppStep || toggleReviewApp.isPending}
              >
                AI-проверка PR&apos;ов
                {hasReviewAppStep && (
                  <span className="ml-auto text-xs text-muted-foreground">уже добавлен</span>
                )}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>

        {flow.steps.length === 0 ? (
          <Card className="p-6 text-center text-sm text-muted-foreground">
            Пока ни одного шага. Добавь приветствие или гайд.
          </Card>
        ) : (
          <DragDropProvider
            onDragEnd={(event) => {
              if (event.canceled) return;
              const { source } = event.operation;
              if (!isSortable(source)) return;
              const { initialIndex, index } = source;
              if (initialIndex === index) return;

              const stepId = source.id as string;
              // Считаем new before/after из old массива исключая текущий step.
              // others — массив без перемещаемого, в позиции `index` его соседи.
              const others = flow.steps.filter((s) => s.id !== stepId);
              const beforeStepId = index > 0 ? others[index - 1].id : null;
              const afterStepId = index < others.length ? others[index].id : null;
              reorderStep.mutate({
                stepId,
                request: { beforeStepId, afterStepId },
              });
            }}
          >
            <ol className="space-y-2">
              {flow.steps.map((step, idx) => (
                <SortableStepRow
                  key={step.id}
                  step={step}
                  index={idx}
                  onEdit={() => handleEdit(step)}
                  onDelete={
                    step.stepType === "GITHUB_REVIEW_APP"
                      ? handleDeleteReviewApp
                      : () => deleteStep.mutate(step.id)
                  }
                  isDeleting={
                    step.stepType === "GITHUB_REVIEW_APP"
                      ? toggleReviewApp.isPending
                      : deleteStep.isPending
                  }
                  onToggleSkippable={(isSkippable) =>
                    setStepIsSkippable.mutate({ stepId: step.id, isSkippable })
                  }
                  isTogglingSkippable={setStepIsSkippable.isPending}
                />
              ))}
            </ol>
          </DragDropProvider>
        )}
      </section>

      <MarkdownStepDialog
        planId={planId}
        step={editingStep}
        open={isDialogOpen}
        onOpenChange={setIsDialogOpen}
      />
    </div>
  );
}

function labelForAutoStep(type: OnboardingStepDto["stepType"]): string {
  switch (type) {
    case "TELEGRAM":
      return "Привязка Telegram (авто)";
    case "GITHUB":
      return "Доступ к GitHub-org (авто)";
    case "NOTIFICATIONS":
      return "Настройка уведомлений (авто)";
    case "GITHUB_REVIEW_APP":
      return "AI-проверка PR'ов (авто)";
    default:
      return "—";
  }
}

type SortableStepRowProps = {
  step: OnboardingStepDto;
  index: number;
  onEdit: () => void;
  onDelete: () => void;
  isDeleting: boolean;
  onToggleSkippable: (isSkippable: boolean) => void;
  isTogglingSkippable: boolean;
};

function SortableStepRow({
  step,
  index,
  onEdit,
  onDelete,
  isDeleting,
  onToggleSkippable,
  isTogglingSkippable,
}: SortableStepRowProps) {
  const { ref, handleRef, isDragging } = useSortable({ id: step.id, index });
  const skippableSwitchId = `step-skippable-${step.id}`;

  return (
    <li ref={ref} className={isDragging ? "opacity-50" : undefined}>
      <Card className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center">
        <button
          ref={handleRef}
          type="button"
          className="cursor-grab touch-none self-start text-muted-foreground hover:text-foreground"
          aria-label="Перетащить"
        >
          <GripVertical className="h-4 w-4" />
        </button>
        <div className="min-w-0 flex-1">
          <div className="text-xs font-medium text-muted-foreground">
            {index + 1}. {step.stepType}
          </div>
          <div className="font-medium">{step.title ?? labelForAutoStep(step.stepType)}</div>
        </div>
        <div className="flex items-center gap-2 self-start sm:self-center">
          <Switch
            id={skippableSwitchId}
            checked={step.isSkippable}
            disabled={isTogglingSkippable}
            onCheckedChange={(checked) => onToggleSkippable(checked)}
            aria-label="Можно пропустить"
          />
          <Label htmlFor={skippableSwitchId} className="text-xs text-muted-foreground">
            Можно пропустить
          </Label>
        </div>
        {(step.stepType === "MARKDOWN" || step.stepType === "GITHUB_REVIEW_APP") && (
          <div className="flex gap-1 self-start sm:self-center">
            {step.stepType === "MARKDOWN" && (
              <Button size="sm" variant="ghost" onClick={onEdit}>
                Изменить
              </Button>
            )}
            <Button size="sm" variant="ghost" disabled={isDeleting} onClick={onDelete}>
              Удалить
            </Button>
          </div>
        )}
      </Card>
    </li>
  );
}
