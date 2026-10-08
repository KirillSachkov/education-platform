"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import {
  Sheet,
  SheetContent,
  SheetTitle,
} from "@/shared/ui/kit/sheet";
import { Loader2 } from "lucide-react";
import { issueDetailQueryOptions } from "@/entities/issue";
import { EditIssueForm } from "./edit-issue-form";

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  issueId: string;
  projectId: string;
  projectName: string;
  courseId: string;
};

export function EditIssueSheet({
  open,
  onOpenChange,
  issueId,
  projectId,
  projectName,
  courseId,
}: Props) {
  const [editorViewMode, setEditorViewMode] = useState<"write" | "split" | "preview">("write");
  const { data: issue, isLoading } = useQuery({
    ...issueDetailQueryOptions(issueId),
    enabled: open && !!issueId,
  });

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        size="wide"
        showCloseButton={false}
        className={
          editorViewMode === "split"
            ? "p-0 gap-0 sm:max-w-[72vw]"
            : "p-0 gap-0 sm:max-w-[56vw]"
        }
      >
        <SheetTitle className="sr-only">Редактирование задачи</SheetTitle>
        {isLoading || !issue ? (
          <div className="flex items-center justify-center h-full">
            <Loader2 className="size-6 animate-spin text-muted-foreground" />
          </div>
        ) : (
          <EditIssueForm
            key={issue.updatedAt}
            issue={issue}
            issueId={issueId}
            projectId={projectId || issue.projectId}
            projectName={projectName}
            courseId={courseId}
            onClose={() => onOpenChange(false)}
            onEditorViewModeChange={setEditorViewMode}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}
