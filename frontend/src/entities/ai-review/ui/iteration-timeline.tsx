"use client";

import type { AiReviewIterationDto } from "../types";
import { IterationCard } from "./iteration-card";
import { cn } from "@/shared/lib/css";
import { Icons } from "@/shared/ui/icons";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/shared/ui/kit/collapsible";
import { useState } from "react";
import type { ReactNode } from "react";

interface IterationTimelineProps {
  /** Iterations as returned by the API (chronological / oldest-first). */
  iterations: AiReviewIterationDto[];
  pullRequestUrl: string;
  /**
   * Render-prop for the per-iteration feedback slot (👍/👎). Lives in the
   * consuming feature/widget layer — entities can't import features (FSD).
   */
  renderFeedback?: (iteration: AiReviewIterationDto) => ReactNode;
}

/**
 * Vertical timeline of AI iterations, newest-first. The latest iteration is
 * always visible; older ones collapse under «Показать историю (N)».
 *
 * The timeline rail is a left border with verdict-coloured dots so the eye can
 * scan verdict progression top-to-bottom (newest → first).
 */
export function IterationTimeline({
  iterations,
  pullRequestUrl,
  renderFeedback,
}: IterationTimelineProps) {
  const [open, setOpen] = useState(false);

  // Newest-first: latest iteration on top, first attempt at the bottom.
  const ordered = iterations.slice().reverse();
  const [latest, ...older] = ordered;

  if (!latest) return null;

  return (
    <div className="relative" data-testid="iteration-list">
      {/* Timeline rail */}
      <div
        aria-hidden
        className="pointer-events-none absolute bottom-3 left-[7px] top-3 w-px bg-border/70"
      />

      <ol className="space-y-3">
        <TimelineRow isLatest>
          <IterationCard
            iteration={latest}
            pullRequestUrl={pullRequestUrl}
            isLatest
            feedbackSlot={renderFeedback?.(latest)}
          />
        </TimelineRow>

        {older.length > 0 ? (
          <li>
            <Collapsible open={open} onOpenChange={setOpen}>
              <CollapsibleTrigger
                className={cn(
                  "group/disc relative ml-7 inline-flex items-center gap-1.5 rounded-full",
                  "border border-border/60 bg-muted/40 px-3 py-1 text-xs font-medium",
                  "text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
                )}
                data-testid="iteration-history-toggle"
              >
                <Icons.chevronDown className="size-3.5 transition-transform duration-200 group-data-[state=open]/disc:rotate-180" />
                {open ? "Свернуть историю" : `Показать историю (${older.length})`}
              </CollapsibleTrigger>

              <CollapsibleContent className="overflow-hidden data-[state=closed]:animate-collapsible-up data-[state=open]:animate-collapsible-down">
                <ol className="mt-3 space-y-3">
                  {older.map((iteration) => (
                    <TimelineRow key={iteration.id}>
                      <IterationCard
                        iteration={iteration}
                        pullRequestUrl={pullRequestUrl}
                        feedbackSlot={renderFeedback?.(iteration)}
                      />
                    </TimelineRow>
                  ))}
                </ol>
              </CollapsibleContent>
            </Collapsible>
          </li>
        ) : null}
      </ol>
    </div>
  );
}

function TimelineRow({ isLatest = false, children }: { isLatest?: boolean; children: ReactNode }) {
  return (
    <li className="relative pl-7">
      <span
        aria-hidden
        className={cn(
          "absolute left-0 top-5 size-3.5 rounded-full border-2 border-card",
          isLatest ? "bg-primary ring-2 ring-primary/20" : "bg-border",
        )}
      />
      {children}
    </li>
  );
}
