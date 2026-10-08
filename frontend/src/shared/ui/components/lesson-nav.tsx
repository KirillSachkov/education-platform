import Link from "next/link";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { cn } from "@/shared/lib/css";

export interface LessonNavItem {
  title: string;
  href: string;
}

interface LessonNavProps {
  /**
   * Primary sequence — the full program order as the author laid it out
   * (materials + issues + quizzes). These render as the big prev/next cards.
   */
  prev: LessonNavItem | null;
  next: LessonNavItem | null;
  /**
   * Optional "tasks-only" jump. Rendered as a compact secondary card row BELOW the
   * primary cards so the two streams read as a clear hierarchy instead of two
   * identical rows (#196 follow-up). Omit on material/quiz pages.
   */
  tasks?: { prev: LessonNavItem | null; next: LessonNavItem | null };
  className?: string;
}

/**
 * Unified lesson/issue/quiz footer navigation.
 *
 * The primary "По программе" cards are always the dominant element. When a
 * page also offers a tasks-only stream (issue pages), it's demoted to a compact
 * secondary card row — lighter visual weight, not a second equal block — so the
 * two never look the same. The "По программе" label only appears when the tasks
 * row is present (otherwise there's nothing to disambiguate against).
 */
export function LessonNav({ prev, next, tasks, className }: LessonNavProps) {
  const hasProgram = Boolean(prev || next);
  const tasksPrev = tasks?.prev ?? null;
  const tasksNext = tasks?.next ?? null;
  const hasTasks = Boolean(tasksPrev || tasksNext);

  if (!hasProgram && !hasTasks) return null;

  return (
    <nav
      aria-label="Навигация по программе"
      className={cn("mt-8 pt-6 border-t border-border/60", className)}
    >
      {hasProgram && (
        <div>
          {hasTasks && (
            <div className="mb-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground/70">
              По программе
            </div>
          )}
          <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 sm:gap-3">
            <PrevCard item={prev} />
            <NextCard item={next} />
          </div>
        </div>
      )}

      {hasTasks && <TasksRow prev={tasksPrev} next={tasksNext} />}
    </nav>
  );
}

function PrevCard({ item }: { item: LessonNavItem | null }) {
  if (!item) return <div className="hidden sm:block" aria-hidden="true" />;
  return (
    <Link
      href={item.href}
      className="group flex items-center gap-3 rounded-2xl border border-border/60 bg-card/30 px-4 py-3 transition-colors hover:border-border hover:bg-card/60 min-w-0"
    >
      <ChevronLeft
        size={18}
        className="shrink-0 text-muted-foreground transition-transform group-hover:-translate-x-0.5 group-hover:text-foreground"
      />
      <div className="min-w-0 flex-1">
        <span className="block text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground/80">
          Предыдущий
        </span>
        <span className="mt-0.5 block truncate text-sm font-medium text-foreground/90 group-hover:text-foreground">
          {item.title}
        </span>
      </div>
    </Link>
  );
}

function NextCard({ item }: { item: LessonNavItem | null }) {
  if (!item) return <div className="hidden sm:block" aria-hidden="true" />;
  return (
    <Link
      href={item.href}
      className="group flex items-center gap-3 rounded-2xl border border-primary/25 bg-primary/5 px-4 py-3 transition-colors hover:border-primary/50 hover:bg-primary/10 min-w-0 sm:text-right"
    >
      <div className="min-w-0 flex-1">
        <span className="block text-[10px] font-semibold uppercase tracking-[0.14em] text-primary/80">
          Следующий
        </span>
        <span className="mt-0.5 block truncate text-sm font-medium text-primary">{item.title}</span>
      </div>
      <ChevronRight
        size={18}
        className="shrink-0 text-primary transition-transform group-hover:translate-x-0.5"
      />
    </Link>
  );
}

/**
 * Compact secondary "tasks-only" jump (#575). Two columns that stack on mobile and
 * mirror the program cards at a lighter weight — each title gets its own column with a
 * real width budget so long «DS-NN: …» titles truncate cleanly instead of colliding in
 * the center of a single flex row (the old inline-line layout read as broken styling).
 */
function TasksRow({ prev, next }: { prev: LessonNavItem | null; next: LessonNavItem | null }) {
  return (
    <div className="mt-3">
      <div className="mb-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground/55">
        Только задачи
      </div>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {prev ? (
          <Link
            href={prev.href}
            className="group flex min-w-0 items-center gap-2 rounded-xl border border-border/40 bg-card/20 px-3 py-2 text-xs text-muted-foreground transition-colors hover:border-border hover:bg-card/40 hover:text-foreground"
          >
            <ChevronLeft
              size={14}
              className="shrink-0 transition-transform group-hover:-translate-x-0.5"
            />
            <span className="truncate">{prev.title}</span>
          </Link>
        ) : (
          <div className="hidden sm:block" aria-hidden="true" />
        )}
        {next ? (
          <Link
            href={next.href}
            className="group flex min-w-0 items-center justify-end gap-2 rounded-xl border border-border/40 bg-card/20 px-3 py-2 text-xs text-muted-foreground transition-colors hover:border-border hover:bg-card/40 hover:text-foreground"
          >
            <span className="truncate">{next.title}</span>
            <ChevronRight
              size={14}
              className="shrink-0 transition-transform group-hover:translate-x-0.5"
            />
          </Link>
        ) : (
          <div className="hidden sm:block" aria-hidden="true" />
        )}
      </div>
    </div>
  );
}
