import type { DeveloperLevel } from "@/entities/level-test";
import { cn } from "@/shared/lib/css";
import { Badge } from "@/shared/ui/kit/badge";
import { DEVELOPER_LEVEL_BADGE_CLASSES, DEVELOPER_LEVEL_LABELS } from "../model/level-visuals";

/** Цветной бейдж уровня Junior/Middle/Senior. */
export function LevelBadge({ level, className }: { level: DeveloperLevel; className?: string }) {
  return (
    <Badge
      variant="outline"
      className={cn("font-semibold", DEVELOPER_LEVEL_BADGE_CLASSES[level], className)}
    >
      {DEVELOPER_LEVEL_LABELS[level]}
    </Badge>
  );
}

interface LevelTestScoreHeaderProps {
  overallPercent: number;
  level: DeveloperLevel;
  subtitle?: string;
}

/** Шапка результата: крупный общий % + бейдж уровня. Общая для тизера и полного разбора. */
export function LevelTestScoreHeader({ overallPercent, level, subtitle }: LevelTestScoreHeaderProps) {
  return (
    <div className="flex flex-col items-center gap-3 text-center">
      <p className="text-sm font-medium uppercase tracking-wide text-muted-foreground">
        Твой результат
      </p>
      <p className="text-6xl font-bold tabular-nums tracking-tight">{overallPercent}%</p>
      <LevelBadge level={level} className="px-3 py-1 text-sm" />
      {subtitle && <p className="text-sm text-muted-foreground">{subtitle}</p>}
    </div>
  );
}
