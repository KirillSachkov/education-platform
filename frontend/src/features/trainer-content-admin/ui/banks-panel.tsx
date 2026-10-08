"use client";

import {
  trainerAdminContentQueryOptions,
  TRAINER_BANK_DIFFICULTIES,
  TRAINER_BANK_PURPOSES,
  TRAINER_BANK_TIERS,
  type AddTrainerTopicBankBody,
  type TrainerTopicBankAdmin,
} from "@/entities/trainer-admin-content";
import { getErrorMessage } from "@/shared/api";
import { TRAINER_DIFFICULTY_VISUALS } from "@/shared/config/trainer";
import { DeleteConfirmDialog } from "@/shared/ui/components/delete-confirm-dialog";
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/ui/kit/select";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { useQuery } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { useState } from "react";
import {
  useAddTrainerTopicBank,
  useDeleteTrainerTopicBank,
  useSetTrainerTopicBankTier,
  useUpdateTrainerTopicBank,
} from "../model/use-bank-mutations";
import { BankQuestionsEditor } from "./bank-questions-editor";

/** Человекочитаемые подписи tier/purpose банка. */
const TIER_LABELS: Record<string, string> = {
  FREE: "Бесплатный",
  PAID: "Полный доступ",
};

const PURPOSE_LABELS: Record<string, string> = {
  STUDY: "Изучение",
  MOCK: "Мок-собес",
};

const NO_DIFFICULTY = "__none__";

interface BanksPanelProps {
  topicId: string;
}

/**
 * Панель банков вопросов выбранной темы (#623): список собственных банков с
 * tier/difficulty/purpose + число залитых вопросов + форма создания нового
 * (пустого) банка. Банки создаются ПУСТЫМИ — вопросы добавляются в редакторе
 * вопросов банка (см. `BankQuestionsEditor`). Tier/difficulty/purpose
 * редактируются инлайн.
 */
export function BanksPanel({ topicId }: BanksPanelProps) {
  const { data: banks, isLoading, error } = useQuery(
    trainerAdminContentQueryOptions.banksOptions(topicId),
  );

  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  if (error) {
    return (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить банки")}
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <AddBankForm topicId={topicId} />

      {!banks || banks.length === 0 ? (
        <EmptyState
          variant="dashed"
          icon={Icons.database}
          title="Банков пока нет"
          description="Создайте банк вопросов, чтобы тема появилась в тренажёре. Вопросы добавляются в редакторе банка."
        />
      ) : (
        <ul className="space-y-2">
          {banks.map((bank) => (
            <BankRow key={bank.id} topicId={topicId} bank={bank} />
          ))}
        </ul>
      )}
    </div>
  );
}

/** Форма создания нового (пустого) банка: tier + опц. difficulty. */
function AddBankForm({ topicId }: { topicId: string }) {
  const [tier, setTier] = useState<string>("FREE");
  const [difficulty, setDifficulty] = useState<string>(NO_DIFFICULTY);
  const addMutation = useAddTrainerTopicBank();

  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const body: AddTrainerTopicBankBody = {
      tier,
      difficulty: difficulty === NO_DIFFICULTY ? null : difficulty,
    };
    addMutation.mutate(
      { topicId, body },
      {
        onSuccess: () => {
          setTier("FREE");
          setDifficulty(NO_DIFFICULTY);
        },
      },
    );
  };

  return (
    <form
      onSubmit={handleSubmit}
      className="flex flex-col gap-3 rounded-lg border border-border/60 bg-card/40 p-3 sm:flex-row sm:flex-wrap sm:items-end"
    >
      <div className="space-y-1">
        <label htmlFor="add-bank-tier" className="text-xs font-medium text-muted-foreground">
          Доступ
        </label>
        <Select value={tier} onValueChange={setTier}>
          <SelectTrigger id="add-bank-tier" className="w-full sm:w-[150px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {TRAINER_BANK_TIERS.map((t) => (
              <SelectItem key={t} value={t}>
                {TIER_LABELS[t] ?? t}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label htmlFor="add-bank-difficulty" className="text-xs font-medium text-muted-foreground">
          Сложность
        </label>
        <Select value={difficulty} onValueChange={setDifficulty}>
          <SelectTrigger id="add-bank-difficulty" className="w-full sm:w-[160px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={NO_DIFFICULTY}>Не задана</SelectItem>
            {TRAINER_BANK_DIFFICULTIES.map((d) => (
              <SelectItem key={d} value={d}>
                {TRAINER_DIFFICULTY_VISUALS[d]?.label ?? d}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <Button type="submit" className="w-full sm:w-auto" disabled={addMutation.isPending}>
        <Icons.add className="size-4" />
        Создать банк
      </Button>
    </form>
  );
}

/** Одна строка банка: число вопросов + tier/difficulty/purpose + редактор вопросов + удаление. */
function BankRow({ topicId, bank }: { topicId: string; bank: TrainerTopicBankAdmin }) {
  const setTierMutation = useSetTrainerTopicBankTier();
  const updateMutation = useUpdateTrainerTopicBank();
  const deleteMutation = useDeleteTrainerTopicBank();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [questionsOpen, setQuestionsOpen] = useState(false);

  return (
    <li className="rounded-lg border border-border/60 bg-card p-3">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <div className="min-w-0 flex-1 space-y-1.5">
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
            <span className="font-semibold">Банк вопросов</span>
            <span className="text-xs tabular-nums text-muted-foreground">
              {bank.questionCount} вопрос(ов)
            </span>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <Badge variant="secondary" className="text-[11px]">
              {TIER_LABELS[bank.tier] ?? bank.tier}
            </Badge>
            {bank.difficulty && (
              <span
                className={`rounded px-1.5 py-0.5 text-[11px] font-medium ${TRAINER_DIFFICULTY_VISUALS[bank.difficulty]?.badgeClass ?? "bg-muted text-muted-foreground"}`}
              >
                {TRAINER_DIFFICULTY_VISUALS[bank.difficulty]?.label ?? bank.difficulty}
              </span>
            )}
            <Badge variant="outline" className="text-[11px]">
              {PURPOSE_LABELS[bank.purpose] ?? bank.purpose}
            </Badge>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Select
            value={bank.tier}
            onValueChange={(tier) => setTierMutation.mutate({ topicId, bankId: bank.id, tier })}
          >
            <SelectTrigger className="h-9 w-[140px] text-xs" aria-label="Доступ банка">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TRAINER_BANK_TIERS.map((t) => (
                <SelectItem key={t} value={t}>
                  {TIER_LABELS[t] ?? t}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={bank.difficulty ?? NO_DIFFICULTY}
            onValueChange={(d) =>
              updateMutation.mutate({
                topicId,
                bankId: bank.id,
                body: { difficulty: d === NO_DIFFICULTY ? null : d },
              })
            }
          >
            <SelectTrigger className="h-9 w-[150px] text-xs" aria-label="Сложность банка">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NO_DIFFICULTY}>Без сложности</SelectItem>
              {TRAINER_BANK_DIFFICULTIES.map((d) => (
                <SelectItem key={d} value={d}>
                  {TRAINER_DIFFICULTY_VISUALS[d]?.label ?? d}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select
            value={bank.purpose}
            onValueChange={(p) =>
              updateMutation.mutate({ topicId, bankId: bank.id, body: { purpose: p } })
            }
          >
            <SelectTrigger className="h-9 w-[140px] text-xs" aria-label="Назначение банка">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {TRAINER_BANK_PURPOSES.map((p) => (
                <SelectItem key={p} value={p}>
                  {PURPOSE_LABELS[p] ?? p}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Button
            variant="outline"
            size="sm"
            className="min-touch"
            aria-expanded={questionsOpen}
            onClick={() => setQuestionsOpen((open) => !open)}
          >
            <Icons.quiz className="size-4" />
            Вопросы
            <Icons.chevronDown
              className={cn("size-4 transition-transform", questionsOpen && "rotate-180")}
            />
          </Button>

          <Button
            variant="outline"
            size="icon"
            className="min-touch text-destructive hover:text-destructive"
            aria-label="Удалить банк"
            title="Удалить банк"
            onClick={() => setDeleteOpen(true)}
          >
            <Trash2 size={16} />
          </Button>
        </div>
      </div>

      {questionsOpen && (
        <div className="mt-3">
          <BankQuestionsEditor bankId={bank.id} topicId={topicId} />
        </div>
      )}

      <DeleteConfirmDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        title="Удалить банк?"
        description="Банк удаляется вместе со всеми его вопросами. Это действие необратимо."
        confirmLabel="Удалить"
        isPending={deleteMutation.isPending}
        onConfirm={() => deleteMutation.mutateAsync({ topicId, bankId: bank.id })}
      />
    </li>
  );
}
