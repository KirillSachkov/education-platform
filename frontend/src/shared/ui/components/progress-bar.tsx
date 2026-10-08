import { cn } from "@/shared/lib/css";
import { Progress } from "@/shared/ui/kit/progress";

interface ProgressBarProps {
  value: number;
  className?: string;
}

export function ProgressBar({ value, className }: ProgressBarProps) {
  return (
    <Progress
      value={Math.min(100, Math.max(0, value))}
      className={cn("h-2 bg-secondary", className)}
    />
  );
}
