"use client";

import { Button } from "@/shared/ui/kit/button";
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
import { Archive, ArchiveRestore } from "lucide-react";
import { useState } from "react";
import { useArchiveIssue } from "../model/use-archive-issue";
import { usePublishIssue } from "../model/use-publish-issue";
import { useRestoreIssue } from "../model/use-restore-issue";

interface IssueStatusActionsProps {
  status: string;
  issueId: string;
  projectId: string;
}

export function IssueStatusActions({
  status,
  issueId,
  projectId,
}: IssueStatusActionsProps) {
  const { publishIssue, isPending: isPublishing } = usePublishIssue(projectId);
  const { archiveIssue, isPending: isArchiving } = useArchiveIssue(projectId);
  const { restoreIssue, isPending: isRestoring } = useRestoreIssue(projectId);
  const [publishOpen, setPublishOpen] = useState(false);
  const [notifySubscribers, setNotifySubscribers] = useState(true);

  const isPending = isPublishing || isArchiving || isRestoring;

  const handlePublish = async () => {
    await publishIssue({ issueId, notifySubscribers });
    setPublishOpen(false);
  };

  if (status === "DRAFT") {
    return (
      <>
        <Button
          type="button"
          size="sm"
          disabled={isPending}
          onClick={() => setPublishOpen(true)}
          className="bg-orange text-primary-foreground hover:bg-orange/90"
        >
          {isPublishing ? "Публикация..." : "Опубликовать"}
        </Button>
        <AlertDialog
          open={publishOpen}
          onOpenChange={(open) => {
            setPublishOpen(open);
            if (!open) setNotifySubscribers(true);
          }}
        >
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Опубликовать задание?</AlertDialogTitle>
              <AlertDialogDescription>
                Задание станет видно ученикам.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <div className="flex items-center gap-2 py-2">
              <input
                id="notify-on-publish-issue"
                type="checkbox"
                className="h-4 w-4 rounded border-border"
                checked={notifySubscribers}
                onChange={(e) => setNotifySubscribers(e.target.checked)}
              />
              <label
                htmlFor="notify-on-publish-issue"
                className="text-sm select-none"
              >
                Уведомить подписчиков курса
              </label>
            </div>
            <AlertDialogFooter>
              <AlertDialogCancel>Отмена</AlertDialogCancel>
              <AlertDialogAction
                onClick={(e) => {
                  e.preventDefault();
                  void handlePublish();
                }}
              >
                Опубликовать
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </>
    );
  }

  if (status === "PUBLISHED") {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={isPending}
        onClick={() => archiveIssue(issueId)}
      >
        <Archive size={14} className="mr-1" />
        {isArchiving ? "..." : "Архивировать"}
      </Button>
    );
  }

  if (status === "ARCHIVED") {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={isPending}
        onClick={() => restoreIssue(issueId)}
      >
        <ArchiveRestore size={14} className="mr-1" />
        {isRestoring ? "..." : "Восстановить"}
      </Button>
    );
  }

  return null;
}
