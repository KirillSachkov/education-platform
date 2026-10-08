"use client";

/**
 * Reusable dropdown submenu для смены уровня доступа материала
 * («три точки» → подменю с 4 опциями). Stateless — caller передаёт
 * текущий {@link value}, обработчик {@link onChange}, и флаг
 * {@link isPending}. Submenu лежит в shared/ui, чтобы его могли
 * использовать features материалов и course-builder без cross-feature
 * импортов (FSD layer rule).
 */

import { Check, Globe, Lock, ShieldCheck } from "lucide-react";
import {
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuItem,
} from "@/shared/ui/kit/dropdown-menu";
import { cn } from "@/shared/lib/css";
import type { ContentAccessType } from "./access-type-selector";

interface Option {
  value: ContentAccessType;
  label: string;
  icon: typeof Globe;
}

const OPTIONS: ReadonlyArray<Option> = [
  { value: "PUBLIC", label: "Публичный", icon: Globe },
  { value: "REGISTERED", label: "Для зарегистрированных", icon: ShieldCheck },
  { value: "ENROLLED", label: "По плану", icon: Lock },
];

/**
 * Краткий ярлык AccessType для inline UI (например, кнопка «Доступ: Пробный»).
 */
export function getAccessTypeShortLabel(value: ContentAccessType): string {
  return OPTIONS.find((o) => o.value === value)?.label ?? value;
}

/**
 * 4 элемента меню (без обёртки) — используется как для top-level DropdownMenu,
 * так и для submenu (внутри другого DropdownMenu). Caller сам выбирает обёртку.
 */
export interface MaterialAccessTypeMenuItemsProps {
  value: ContentAccessType;
  onChange: (value: ContentAccessType) => void;
  disabled?: boolean;
}

export function MaterialAccessTypeMenuItems({
  value,
  onChange,
  disabled,
}: MaterialAccessTypeMenuItemsProps) {
  return (
    <>
      {OPTIONS.map((option) => {
        const Icon = option.icon;
        const active = value === option.value;
        return (
          <DropdownMenuItem
            key={option.value}
            disabled={disabled}
            onSelect={(e) => {
              if (active) {
                e.preventDefault();
                return;
              }
              onChange(option.value);
            }}
          >
            {active ? (
              <Check size={14} className="text-primary" />
            ) : (
              <Icon size={14} className="text-muted-foreground" />
            )}
            <span className={cn(active && "font-medium")}>{option.label}</span>
          </DropdownMenuItem>
        );
      })}
    </>
  );
}

export interface MaterialAccessTypeSubmenuProps {
  value: ContentAccessType;
  onChange: (value: ContentAccessType) => void;
  disabled?: boolean;
  /** Текст trigger'а (default: «Уровень доступа»). */
  triggerLabel?: string;
  triggerIcon?: typeof Globe;
}

export function MaterialAccessTypeSubmenu({
  value,
  onChange,
  disabled,
  triggerLabel = "Уровень доступа",
  triggerIcon: TriggerIcon = ShieldCheck,
}: MaterialAccessTypeSubmenuProps) {
  return (
    <DropdownMenuSub>
      <DropdownMenuSubTrigger disabled={disabled}>
        <TriggerIcon size={14} />
        {triggerLabel}
      </DropdownMenuSubTrigger>
      <DropdownMenuSubContent>
        <MaterialAccessTypeMenuItems value={value} onChange={onChange} disabled={disabled} />
      </DropdownMenuSubContent>
    </DropdownMenuSub>
  );
}
