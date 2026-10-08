import {
  FileText,
  NotebookPen,
  Play,
  Radio,
  type LucideIcon,
} from "lucide-react";
import type {
  MaterialAccessType,
  MaterialKind,
  MaterialStatus,
  MaterialSummaryDto,
} from "../types";

type BadgeConfig = {
  label: string;
  className: string;
  iconName: MaterialKindIconName;
  icon: LucideIcon;
  iconBgClassName: string;
};

type BadgeNoIcon = {
  label: string;
  className: string;
};

export type MaterialKindIconName = "article" | "video" | "note" | "stream";

const kindBadgeMap: Record<MaterialKind, BadgeConfig> = {
  ARTICLE: {
    label: "Статья",
    className: "border-blue/30 bg-blue/10 text-blue",
    iconName: "article",
    icon: FileText,
    iconBgClassName: "bg-blue/10 border border-blue/25 text-blue",
  },
  VIDEO: {
    label: "Видео",
    className: "border-teal/30 bg-teal/10 text-teal",
    iconName: "video",
    icon: Play,
    iconBgClassName: "bg-teal-dim border border-teal/25 text-teal",
  },
  NOTE: {
    label: "Заметка",
    className: "border-yellow/30 bg-yellow/10 text-yellow",
    iconName: "note",
    icon: NotebookPen,
    iconBgClassName: "bg-yellow/10 border border-yellow/25 text-yellow",
  },
  STREAM: {
    label: "Эфир",
    className: "border-pink-500/30 bg-pink-500/10 text-pink-400",
    iconName: "stream",
    icon: Radio,
    iconBgClassName: "bg-pink-500/10 border border-pink-500/25 text-pink-400",
  },
};

const statusBadgeMap: Record<MaterialStatus, BadgeNoIcon> = {
  DRAFT: {
    label: "Черновик",
    className: "border-yellow/30 bg-yellow/10 text-yellow",
  },
  PUBLISHED: {
    label: "Опубликован",
    className: "border-border/50 bg-transparent text-muted-foreground/60",
  },
  ARCHIVED: {
    label: "Архив",
    className: "border-border bg-secondary text-muted-foreground",
  },
};

const accessBadgeMap: Record<MaterialAccessType, BadgeNoIcon> = {
  PUBLIC: {
    label: "Публичный",
    className: "border-teal/30 bg-teal/10 text-teal",
  },
  REGISTERED: {
    label: "Для зарегистрированных",
    className: "border-blue/30 bg-blue/10 text-blue",
  },
  ENROLLED: {
    label: "По записи",
    className: "border-orange/30 bg-orange/10 text-orange",
  },
};

export function getMaterialKindBadge(kind: MaterialKind): BadgeConfig {
  return kindBadgeMap[kind] ?? kindBadgeMap.ARTICLE;
}

export function getMaterialStatusBadge(status: MaterialStatus): BadgeNoIcon {
  return statusBadgeMap[status];
}

export function getMaterialAccessBadge(
  accessType: MaterialAccessType,
): BadgeNoIcon {
  return accessBadgeMap[accessType];
}

export function getMaterialSortTimestamp(
  material: Pick<MaterialSummaryDto, "updatedAt" | "createdAt">,
) {
  return new Date(material.updatedAt || material.createdAt).getTime();
}

/**
 * Общий список kind-фильтров для pills: «Все» + реальные kind'ы.
 * Используется везде, где есть фильтр материалов по типу (лента, course builder,
 * страница пространства). Порядок и лейблы — единый источник правды.
 */
export const MATERIAL_KINDS_WITH_ALL: Array<{
  value: MaterialKind | "all";
  label: string;
}> = [
  { value: "all", label: "Все" },
  { value: "ARTICLE", label: "Статьи" },
  { value: "VIDEO", label: "Видео" },
  { value: "NOTE", label: "Заметки" },
  { value: "STREAM", label: "Эфиры" },
];
