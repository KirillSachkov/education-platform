import { TRAINER_STAT_ICONS, type TrainerStatConcept } from "@/shared/config/trainer";
import { Icons } from "@/shared/ui/icons";

/**
 * Иконка стат-концепта из единого реестра `TRAINER_STAT_ICONS` (#568). Гарантирует,
 * что один и тот же показатель (streak / точность / SRS / …) обозначается одной и
 * той же иконкой во всех разделах тренажёра. Не импортируй lucide/Icons.* для статов
 * напрямую — только через концепт.
 */
export function TrainerStatIcon({
  concept,
  className,
}: {
  concept: TrainerStatConcept;
  className?: string;
}) {
  const Icon = Icons[TRAINER_STAT_ICONS[concept] as keyof typeof Icons];
  return Icon ? <Icon className={className} aria-hidden /> : null;
}
