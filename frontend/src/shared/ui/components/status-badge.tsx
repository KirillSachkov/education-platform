import { cn } from "@/shared/lib/css";
import type {
  EnrollmentRequestStatus,
  IssueProgressStatus,
  PublicationStatus,
  SubmissionReviewStatus,
} from "@/shared/types";
import { Badge } from "@/shared/ui/kit/badge";

type StatusType =
  | IssueProgressStatus
  | PublicationStatus
  | SubmissionReviewStatus
  | EnrollmentRequestStatus
  | "EASY"
  | "MEDIUM"
  | "HARD"
  | "BEGINNER"
  | "INTERMEDIATE"
  | "ADVANCED";

const statusStyles: Record<StatusType, string> = {
  DRAFT: "border-yellow/30 bg-yellow/10 text-yellow",
  PUBLISHED: "border-border/50 bg-transparent text-muted-foreground/60",
  SUSPENDED: "border-red/30 bg-red/10 text-red",
  ARCHIVED: "border-border bg-secondary text-muted-foreground",
  NOT_STARTED: "border-border bg-secondary text-muted-foreground",
  IN_PROGRESS: "border-yellow/40 bg-yellow/10 text-yellow",
  UNDER_REVIEW: "border-blue/40 bg-blue/10 text-blue",
  REQUESTED_CHANGES: "border-red/40 bg-red/10 text-red",
  COMPLETED: "border-teal/40 bg-teal/10 text-teal",
  PENDING: "border-yellow/40 bg-yellow/10 text-yellow",
  IN_REVIEW: "border-blue/40 bg-blue/10 text-blue",
  APPROVED: "border-teal/40 bg-teal/10 text-teal",
  CHANGES_REQUESTED: "border-red/40 bg-red/10 text-red",
  EASY: "border-green/30 bg-green/10 text-green",
  MEDIUM: "border-yellow/30 bg-yellow/10 text-yellow",
  HARD: "border-red/30 bg-red/10 text-red",
  BEGINNER: "border-green/30 bg-green/10 text-green",
  INTERMEDIATE: "border-yellow/30 bg-yellow/10 text-yellow",
  ADVANCED: "border-red/30 bg-red/10 text-red",
  REJECTED: "border-red/40 bg-red/10 text-red",
};

const statusLabels: Record<StatusType, string> = {
  NOT_STARTED: "Не начато",
  IN_PROGRESS: "В работе",
  UNDER_REVIEW: "На проверке",
  REQUESTED_CHANGES: "Нужны правки",
  COMPLETED: "Выполнено",
  PENDING: "Ожидает",
  IN_REVIEW: "На проверке",
  APPROVED: "Одобрено",
  CHANGES_REQUESTED: "Нужны правки",
  DRAFT: "Черновик",
  PUBLISHED: "Опубликован",
  SUSPENDED: "Приостановлен",
  ARCHIVED: "Архив",
  BEGINNER: "Начинающий",
  INTERMEDIATE: "Средний",
  ADVANCED: "Продвинутый",
  EASY: "Лёгкая",
  MEDIUM: "Средняя",
  HARD: "Сложная",
  REJECTED: "Отклонено",
};

interface StatusBadgeProps {
  status: string;
  label?: string;
  icon?: React.ReactNode;
  className?: string;
}

function normalizeStatus(status: string): StatusType {
  // Backend returns UPPER_CASE ("DRAFT"); normalizer also handles legacy PascalCase
  const upper = status.replace(/([a-z])([A-Z])/g, "$1_$2").toUpperCase();
  if (upper in statusStyles) return upper as StatusType;
  return status as StatusType;
}

export function StatusBadge({
  status,
  label,
  icon,
  className,
}: StatusBadgeProps) {
  const normalized = normalizeStatus(status);
  return (
    <Badge
      variant="outline"
      className={cn(statusStyles[normalized], className)}
    >
      {icon}
      {label ?? statusLabels[normalized] ?? status}
    </Badge>
  );
}

export type { StatusType };
