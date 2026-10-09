import type { ReactNode } from "react";
import { Icons } from "@/shared/ui/icons";

interface IssueHeaderProps {
  title: string;
  projectTitle: string;
  status: string;
  maxScore?: number | null;
  action?: ReactNode;
}

export function IssueHeader({ title, projectTitle, action }: IssueHeaderProps) {
  return (
    <div className="mb-6 space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-start gap-3 min-w-0">
          <div className="size-10 rounded-xl bg-orange/10 border border-orange/25 flex items-center justify-center shrink-0 mt-1">
            <Icons.issue size={18} className="text-orange" />
          </div>
          <div className="min-w-0">
            <p className="text-[10px] uppercase tracking-[0.18em] text-muted-foreground font-semibold mb-1">
              Задача
            </p>
            <h1 className="text-2xl font-bold leading-tight">{title}</h1>
          </div>
        </div>
        {action ? <div className="shrink-0">{action}</div> : null}
      </div>
      <div className="flex items-center gap-3 flex-wrap">
        <span className="text-sm text-muted-foreground">Проект: {projectTitle}</span>
      </div>
    </div>
  );
}
