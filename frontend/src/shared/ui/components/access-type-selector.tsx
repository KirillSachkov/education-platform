"use client";

import { cn } from "@/shared/lib/css";
import { Icons, type IconComponent } from "@/shared/ui/icons";

/**
 * Единая модель доступа для контента платформы (совпадает с backend enum
 * {@code EducationContentService.Domain.AccessType}). Используется для
 * материалов и подборок; потенциально — для других сущностей.
 *
 * Issue #358: FREE удалён — бесплатный доступ теперь = system default = REGISTERED.
 */
export type ContentAccessType = "PUBLIC" | "REGISTERED" | "ENROLLED";

export const CONTENT_ACCESS_TYPES = [
  "PUBLIC",
  "REGISTERED",
  "ENROLLED",
] as const satisfies ReadonlyArray<ContentAccessType>;

type AccessOption = {
  value: ContentAccessType;
  label: string;
  /** Подсказка для course-bound контента. */
  hint: string;
  /** Подсказка для orphan-контента (не привязан к курсу). */
  orphanHint: string;
  icon: IconComponent;
  activeClass: string;
};

const ACCESS_OPTIONS: ReadonlyArray<AccessOption> = [
  {
    value: "PUBLIC",
    label: "Публичный",
    hint: "Видно всем",
    orphanHint: "Видно всем",
    icon: Icons.globe,
    activeClass: "border-teal/30 bg-teal/10 text-teal",
  },
  {
    value: "REGISTERED",
    label: "Для зарегистрированных",
    hint: "Нужен аккаунт",
    orphanHint: "Нужен аккаунт",
    icon: Icons.users,
    activeClass: "border-blue/30 bg-blue/10 text-blue",
  },
  {
    value: "ENROLLED",
    label: "По плану",
    hint: "Полная запись на курс",
    orphanHint: "Платный план платформы",
    icon: Icons.locked,
    activeClass: "border-orange/30 bg-orange/10 text-orange",
  },
];

export interface AccessTypeSelectorProps {
  value: ContentAccessType;
  onChange: (value: ContentAccessType) => void;
  /**
   * Привязан ли контент к курсу. После plan-bound рефакторинга (#77) ENROLLED
   * больше не блокируется для orphan-контента — гейт уходит на планы платформы. Флаг
   * нужен только для подсказок: для orphan показывается «платный план платформы»,
   * для course-bound — «полная запись».
   */
  hasCourseBinding: boolean;
  className?: string;
}

export function AccessTypeSelector({
  value,
  onChange,
  hasCourseBinding,
  className,
}: AccessTypeSelectorProps) {
  return (
    <div
      className={cn(
        "grid grid-cols-3 gap-2 rounded-xl border border-border/70 bg-card/60 p-2",
        className,
      )}
    >
      {ACCESS_OPTIONS.map((option) => {
        const active = value === option.value;
        const Icon = option.icon;
        const hint = hasCourseBinding ? option.hint : option.orphanHint;
        return (
          <button
            key={option.value}
            type="button"
            onClick={() => onChange(option.value)}
            aria-pressed={active}
            className={cn(
              "inline-flex flex-col items-center justify-center gap-0.5 rounded-lg border px-3 py-1.5 transition-colors",
              active
                ? option.activeClass
                : "border-border/70 text-muted-foreground hover:text-foreground",
            )}
          >
            <span className="inline-flex items-center gap-1.5 text-xs font-medium">
              <Icon size={12} />
              {option.label}
            </span>
            <span className="text-[10px] text-muted-foreground">{hint}</span>
          </button>
        );
      })}
    </div>
  );
}
