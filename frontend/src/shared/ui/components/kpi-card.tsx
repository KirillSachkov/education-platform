import type { ReactNode } from "react";
import { Card, CardContent } from "@/shared/ui/kit/card";
import { Skeleton } from "@/shared/ui/kit/skeleton";

type KpiCardProps = {
  label: string;
  value: ReactNode;
  hint?: ReactNode;
  isLoading?: boolean;
};

export function KpiCard({ label, value, hint, isLoading = false }: KpiCardProps) {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="text-sm text-muted-foreground">{label}</div>
        {isLoading ? (
          <Skeleton className="mt-2 h-7 w-20" />
        ) : (
          <div className="mt-2 text-2xl font-semibold tabular-nums">{value}</div>
        )}
        {hint ? <div className="mt-1 text-xs text-muted-foreground">{hint}</div> : null}
      </CardContent>
    </Card>
  );
}
