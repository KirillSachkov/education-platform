"use client";
import { Icons } from "@/shared/ui/icons";

import type { AdminUserSubmission } from "@/entities/admin-cross-service";
import { adminCrossServiceQueryOptions } from "@/entities/admin-cross-service";
import { useQuery } from "@tanstack/react-query";
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
import { Badge } from "@/shared/ui/kit/badge";
import { Button } from "@/shared/ui/kit/button";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Skeleton } from "@/shared/ui/kit/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/shared/ui/kit/table";
import { formatDate } from "../../lib/format";
import { useMarkSubmissionComplete } from "../../model/use-mark-submission-complete";

type SubmissionsTabProps = {
  userId: string;
};

export function SubmissionsTab({ userId }: SubmissionsTabProps) {
  const query = useQuery(adminCrossServiceQueryOptions.getSubmissionsOptions(userId));

  if (query.isLoading) return <Skeletons />;
  if (query.isError) return <EmptyState variant="card" title="Не удалось загрузить сабмишены" icon={Icons.list} />;

  const items = query.data ?? [];
  if (items.length === 0) return <EmptyState variant="card" title="Сабмишенов нет" icon={Icons.list} />;

  return (
    <Card>
      <CardContent className="p-0">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Задача</TableHead>
              <TableHead>Попытка</TableHead>
              <TableHead>Статус</TableHead>
              <TableHead>AI</TableHead>
              <TableHead>Отправлен</TableHead>
              <TableHead>Проверен</TableHead>
              <TableHead className="text-right">Действие</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {items.map((s) => (
              <TableRow key={s.id}>
                <TableCell className="font-mono text-xs">{s.issueId}</TableCell>
                <TableCell>#{s.attemptNumber}</TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-center gap-1">
                    <Badge variant="outline" className="text-xs">
                      {s.reviewStatus}
                    </Badge>
                    {!s.readyForHumanReview && s.reviewStatus !== "APPROVED" ? (
                      <Badge
                        variant="outline"
                        className="border-amber-400/40 bg-amber-400/10 text-[10px] text-amber-300"
                        title="Сабмишен на AI-гейте (ready_for_human_review=false) — не попадает в инбокс проверок, пока студент не «Отправит автору». Можно засчитать вручную."
                      >
                        вне инбокса
                      </Badge>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell className="text-xs text-muted-foreground">
                  {s.latestAiVerdict ?? s.aiReviewStatus ?? "—"}
                </TableCell>
                <TableCell className="text-sm">{formatDate(s.submittedAt)}</TableCell>
                <TableCell className="text-sm">{formatDate(s.reviewedAt)}</TableCell>
                <TableCell className="text-right">
                  <MarkCompleteAction userId={userId} submission={s} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function MarkCompleteAction({
  userId,
  submission,
}: {
  userId: string;
  submission: AdminUserSubmission;
}) {
  const { mutateAsync, isPending } = useMarkSubmissionComplete(userId);

  if (submission.reviewStatus === "APPROVED") {
    return <span className="text-xs text-muted-foreground">Принято</span>;
  }

  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          className="text-green border-green/30 hover:bg-green/10"
          disabled={isPending}
        >
          {isPending ? (
            <Icons.loading size={13} className="animate-spin" />
          ) : (
            <Icons.completed size={13} />
          )}
          Отметить выполненным
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Засчитать задание вручную?</AlertDialogTitle>
          <AlertDialogDescription>
            Задание будет принято независимо от статуса AI-проверки (force-approve). Студенту
            начислится XP, прогресс по задаче перейдёт в «Выполнено». Действие отражается в
            истории проверок.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={isPending}>Отмена</AlertDialogCancel>
          <AlertDialogAction
            disabled={isPending}
            onClick={() =>
              void mutateAsync({ courseId: submission.courseId, submissionId: submission.id })
            }
          >
            Засчитать
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

function Skeletons() {
  return (
    <div className="space-y-2">
      {[1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-12 w-full" />
      ))}
    </div>
  );
}
