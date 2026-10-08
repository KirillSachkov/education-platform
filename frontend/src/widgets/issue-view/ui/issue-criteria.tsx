import { CheckCircle2 } from "lucide-react";
import { Card } from "@/shared/ui/kit/card";
import { CardTitle } from "@/shared/ui/kit/card";
import type { IssueProgressStatus } from "@/entities/course-progress";

interface IssueCriteriaProps {
  criteria: string[];
  status: IssueProgressStatus;
}

export function IssueCriteria({ criteria, status }: IssueCriteriaProps) {
  if (criteria.length === 0) return null;

  return (
    <Card className="p-5 mb-4 gap-3">
      <CardTitle className="text-sm flex items-center gap-2">
        <CheckCircle2 size={14} className="text-muted-foreground" /> Критерии
        приёмки
      </CardTitle>
      <div className="space-y-2">
        {criteria.map((criterion, i) => (
          <div key={i} className="flex items-start gap-2.5">
            <div
              className={`size-4 rounded flex items-center justify-center shrink-0 mt-0.5 border ${
                status === "COMPLETED"
                  ? "bg-teal/10 border-teal/40"
                  : "bg-secondary border-border"
              }`}
            >
              {status === "COMPLETED" && (
                <CheckCircle2 size={10} className="text-teal" />
              )}
            </div>
            <p className="text-sm text-foreground/85 leading-relaxed">
              {criterion}
            </p>
          </div>
        ))}
      </div>
    </Card>
  );
}
