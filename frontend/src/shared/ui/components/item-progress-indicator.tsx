import type {
  IssueProgressStatus,
  MaterialProgressStatus,
} from "@/shared/types";
import { Check } from "lucide-react";
import { cn } from "@/shared/lib/css";

type ItemType = "material" | "issue" | "quiz";
type DetailTier = "compact" | "standard";
type AnyStatus = IssueProgressStatus | MaterialProgressStatus;

interface ItemProgressIndicatorProps {
  itemType: ItemType;
  status: AnyStatus;
  tier?: DetailTier;
  isActive?: boolean;
  className?: string;
}

export function getProgressLabel(status: AnyStatus): string | null {
  switch (status) {
    case "IN_PROGRESS":
      return "В работе";
    case "UNDER_REVIEW":
      return "На проверке";
    case "REQUESTED_CHANGES":
      return "Нужны правки";
    default:
      return null;
  }
}

export function getProgressColor(status: AnyStatus): string {
  switch (status) {
    case "IN_PROGRESS":
      return "text-yellow";
    case "UNDER_REVIEW":
      return "text-blue";
    case "REQUESTED_CHANGES":
      return "text-red";
    case "COMPLETED":
    case "VIEWED":
      return "text-green";
    default:
      return "text-muted-foreground";
  }
}

function isCompleted(status: AnyStatus): boolean {
  return status === "COMPLETED" || status === "VIEWED";
}

function isInProgress(status: AnyStatus): boolean {
  return (
    status === "IN_PROGRESS" ||
    status === "UNDER_REVIEW" ||
    status === "REQUESTED_CHANGES"
  );
}

// All indicators render at a consistent 14×14 footprint so material and issue
// rows share the same visual rhythm in mixed lists.
const BASE_SIZE = "w-3.5 h-3.5 shrink-0";
const MATERIAL_SHAPE = "rounded-full";
const ISSUE_SHAPE = "rounded-[3px]";

export function ItemProgressIndicator({
  itemType,
  status,
  tier = "compact",
  isActive = false,
  className,
}: ItemProgressIndicatorProps) {
  const shape = itemType === "issue" ? ISSUE_SHAPE : MATERIAL_SHAPE;

  // Quiz branch — triangle (the third shape: ○ material · ▢ issue · △ quiz).
  // Reuses the same green-done / primary-active / muted-idle palette via
  // currentColor so a quiz row stays visually quiet in the curriculum list
  // instead of a coloured icon + tinted title.
  if (itemType === "quiz") {
    const done = isCompleted(status);
    return (
      <span
        className={cn(
          BASE_SIZE,
          "flex items-center justify-center",
          done ? "text-green" : isActive ? "text-primary" : "text-muted-foreground/50",
          className,
        )}
        aria-hidden="true"
      >
        <svg viewBox="0 0 14 14" fill="none" className="w-full h-full">
          <path
            d="M7 2 L12.5 12 L1.5 12 Z"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinejoin="round"
            className={done ? "fill-green/15" : isActive ? "fill-primary/15" : "fill-transparent"}
          />
          {done && (
            <path
              d="M5.4 8.6 L6.5 9.8 L8.7 7.2"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          )}
        </svg>
      </span>
    );
  }

  // Completed — outlined green shape (circle/square) with a small inner check.
  // When this item is also the active route, layer a primary ring + glow so
  // navigation context isn't lost on already-completed items.
  if (isCompleted(status)) {
    return (
      <span
        className={cn(
          BASE_SIZE,
          shape,
          "flex items-center justify-center border-[1.5px] border-green/70 bg-green/15",
          isActive && "ring-1 ring-primary shadow-[0_0_6px] shadow-primary/40",
          className,
        )}
      >
        <Check size={9} strokeWidth={3} className="text-green" />
      </span>
    );
  }

  // Issue branch
  if (itemType === "issue") {
    if (tier === "compact" && isInProgress(status)) {
      return (
        <span
          className={cn(
            BASE_SIZE,
            ISSUE_SHAPE,
            "border-[1.5px]",
            isActive
              ? "border-primary bg-primary/20 shadow-[0_0_6px] shadow-primary/40"
              : "border-primary/70 bg-primary/15",
            className,
          )}
        />
      );
    }

    if (tier === "standard" && isInProgress(status)) {
      return (
        <span
          className={cn(
            BASE_SIZE,
            ISSUE_SHAPE,
            "border-[1.5px]",
            status === "IN_PROGRESS" && "border-yellow bg-yellow/20",
            status === "UNDER_REVIEW" && "border-blue bg-blue/20",
            status === "REQUESTED_CHANGES" && "border-red bg-red/20",
            className,
          )}
        />
      );
    }

    // Issue not started
    return (
      <span
        className={cn(
          BASE_SIZE,
          ISSUE_SHAPE,
          "border-[1.5px]",
          isActive
            ? "border-primary bg-primary/10 shadow-[0_0_6px] shadow-primary/40"
            : "border-muted-foreground/50",
          className,
        )}
      />
    );
  }

  // Material active — circle with primary border and center dot + soft glow
  if (isActive) {
    return (
      <span
        className={cn(
          BASE_SIZE,
          shape,
          "border-[1.5px] border-primary relative flex items-center justify-center shadow-[0_0_6px] shadow-primary/40",
          className,
        )}
      >
        <span className="w-[6px] h-[6px] rounded-full bg-primary" />
      </span>
    );
  }

  // Material not viewed
  return (
    <span
      className={cn(
        BASE_SIZE,
        MATERIAL_SHAPE,
        "border-[1.5px] border-muted-foreground/50",
        className,
      )}
    />
  );
}
