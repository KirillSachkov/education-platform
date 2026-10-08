"use client";

import {
  type MockInterviewManageItem,
  mockInterviewsQueryOptions,
} from "@/entities/mock-interview";
import { getErrorMessage } from "@/shared/api";
import { cn } from "@/shared/lib/css";
import { pluralize } from "@/shared/lib/pluralize";
import { StatusBadge } from "@/shared/ui/components/status-badge";
import { Icons } from "@/shared/ui/icons";
import { Button } from "@/shared/ui/kit/button";
import { Card } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { CreateMockInterviewDialog } from "./create-mock-interview-dialog";
import { MockInterviewEditor } from "./mock-interview-editor";

/**
 * Описание размера набора в списке: «N из M вопросов» если задана подвыборка,
 * иначе «все M вопросов».
 */
function describeSize(item: MockInterviewManageItem): string {
  const noun = pluralize(item.questionCount, "вопрос", "вопроса", "вопросов");
  if (item.questionsPerSession == null) {
    return `все ${item.questionCount} ${noun}`;
  }
  return `${item.questionsPerSession} из ${item.questionCount} ${noun}`;
}

/**
 * Страница «Мок-собесы» админа (#585): список кураторских подборок (включая
 * DRAFT) с размером набора и статусом; клик раскрывает инлайн-редактор (зеркало
 * /author/quizzes). «Создать мок-собес» — диалог с названием → DRAFT → редактор.
 */
export function MockInterviewsManager() {
  const { data: interviews, isLoading, error } = useQuery(mockInterviewsQueryOptions.manageOptions());
  const [createOpen, setCreateOpen] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  let body: React.ReactNode;
  if (isLoading) {
    body = (
      <div className="space-y-3">
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-20 w-full" />
        <Skeleton className="h-20 w-full" />
      </div>
    );
  } else if (error) {
    body = (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить мок-собесы")}
      </p>
    );
  } else if (!interviews || interviews.length === 0) {
    body = (
      <EmptyState
        variant="dashed"
        icon={Icons.briefcase}
        title="Мок-собесов пока нет"
        description="Создайте кураторскую подборку вопросов под собеседование — выберите вопросы из банка и задайте, сколько показывать за сессию."
        action={
          <Button onClick={() => setCreateOpen(true)}>
            <Icons.add className="size-4" />
            Создать мок-собес
          </Button>
        }
      />
    );
  } else {
    body = (
      <div className="space-y-3">
        {interviews.map((interview) => (
          <MockInterviewCard
            key={interview.id}
            interview={interview}
            isExpanded={expandedId === interview.id}
            onToggle={() =>
              setExpandedId((current) => (current === interview.id ? null : interview.id))
            }
          />
        ))}
      </div>
    );
  }

  return (
    <div className="mx-auto mt-8 max-w-5xl space-y-6 px-4 pb-16">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Мок-собесы</h1>
          <p className="text-sm text-muted-foreground">
            Кураторские подборки вопросов под собеседование: выберите вопросы из банка и задайте
            размер случайной выборки на сессию
          </p>
        </div>
        {interviews && interviews.length > 0 && (
          <Button onClick={() => setCreateOpen(true)}>
            <Icons.add className="size-4" />
            Создать мок-собес
          </Button>
        )}
      </header>

      {body}

      <CreateMockInterviewDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        onCreated={(interviewId) => setExpandedId(interviewId)}
      />
    </div>
  );
}

function MockInterviewCard({
  interview,
  isExpanded,
  onToggle,
}: {
  interview: MockInterviewManageItem;
  isExpanded: boolean;
  onToggle: () => void;
}) {
  return (
    <Card className="gap-0 overflow-hidden p-0">
      <button
        type="button"
        onClick={onToggle}
        className="group flex w-full items-center gap-3 px-4 py-3.5 text-left transition-colors hover:bg-accent/30 sm:px-5"
        aria-expanded={isExpanded}
      >
        <span className="flex size-8 shrink-0 items-center justify-center rounded-full bg-primary/15">
          <Icons.briefcase size={15} className="text-primary" />
        </span>
        <span className="min-w-0 flex-1 space-y-1">
          <span className="block truncate text-sm font-semibold">{interview.title}</span>
          <span className="flex flex-wrap items-center gap-2">
            <StatusBadge status={interview.isPublished ? "PUBLISHED" : "DRAFT"} />
            <span className="text-[11px] tabular-nums text-muted-foreground">
              {describeSize(interview)}
            </span>
          </span>
        </span>
        <Icons.chevronDown
          size={14}
          className={cn(
            "shrink-0 text-muted-foreground/50 transition-transform",
            isExpanded && "rotate-180",
          )}
        />
      </button>

      {isExpanded && (
        <div className="border-t border-border/60 px-4 py-4 sm:px-5">
          <MockInterviewEditorPanel interviewId={interview.id} />
        </div>
      )}
    </Card>
  );
}

function MockInterviewEditorPanel({ interviewId }: { interviewId: string }) {
  const {
    data: interview,
    isLoading,
    error,
  } = useQuery(mockInterviewsQueryOptions.builderOptions(interviewId));

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-10 w-full max-w-xl" />
        <Skeleton className="h-32 w-full" />
      </div>
    );
  }

  if (error || !interview) {
    return (
      <p className="text-sm text-destructive">
        {getErrorMessage(error, "Не удалось загрузить мок-собес")}
      </p>
    );
  }

  return <MockInterviewEditor key={interview.id} interview={interview} />;
}
