import type { SearchEducationDocumentDto } from "@/entities/search";
import { EntityTypes, type EntityType } from "@/shared/config/entity-types";
import { routes } from "@/shared/config/routes";
import { Icons, type IconComponent } from "@/shared/ui/icons";

type BreadcrumbItem = {
  label: string;
  href?: string;
};

type SearchVisual = {
  icon: IconComponent;
  toneClassName: string;
  iconClassName: string;
  label: string;
};

const UNKNOWN_SEARCH_VISUAL: SearchVisual = {
  icon: Icons.help,
  label: "Документ",
  toneClassName: "border border-border/50 bg-secondary/50",
  iconClassName: "text-muted-foreground",
};

const SEARCH_ENTITY_VISUALS: Record<EntityType, SearchVisual> = {
  [EntityTypes.COURSE]: {
    icon: Icons.course,
    label: "Курс",
    toneClassName: "border border-purple-500/20 bg-purple-500/10",
    iconClassName: "text-purple",
  },
  [EntityTypes.MODULE]: {
    icon: Icons.module,
    label: "Модуль",
    toneClassName: "border border-blue-500/20 bg-blue-500/10",
    iconClassName: "text-blue",
  },
  [EntityTypes.PROJECT]: {
    icon: Icons.project,
    label: "Проект",
    toneClassName: "border border-orange-500/20 bg-orange-500/10",
    iconClassName: "text-orange",
  },
  [EntityTypes.MATERIAL]: {
    icon: Icons.lesson,
    label: "Материал",
    toneClassName: "border border-teal/20 bg-teal/10",
    iconClassName: "text-teal",
  },
  [EntityTypes.ISSUE]: {
    icon: Icons.issue,
    label: "Задача",
    toneClassName: "border border-orange/20 bg-orange/10",
    iconClassName: "text-orange",
  },
  [EntityTypes.QUIZ]: {
    icon: Icons.help,
    label: "Тест",
    toneClassName: "border border-blue-500/20 bg-blue-500/10",
    iconClassName: "text-blue",
  },
  [EntityTypes.COLLECTION]: {
    icon: Icons.layers,
    label: "Подборка",
    toneClassName: "border border-primary/20 bg-primary/10",
    iconClassName: "text-primary",
  },
};

function getSectionBreadcrumb(document: SearchEducationDocumentDto) {
  if (document.moduleId && document.moduleTitle && document.courseSlug) {
    return {
      label: document.moduleTitle,
      href: routes.courseModule(document.courseSlug, document.moduleId),
    } satisfies BreadcrumbItem;
  }

  if (document.projectId && document.projectTitle && document.courseSlug) {
    return {
      label: document.projectTitle,
      href: routes.courseProject(document.courseSlug, document.projectId),
    } satisfies BreadcrumbItem;
  }

  return null;
}

const MATERIAL_KIND_VISUALS: Record<string, SearchVisual> = {
  ARTICLE: {
    icon: Icons.article,
    label: "Статья",
    toneClassName: "border border-blue/25 bg-blue/10",
    iconClassName: "text-blue",
  },
  VIDEO: {
    icon: Icons.play,
    label: "Видео",
    toneClassName: "border border-teal/25 bg-teal-dim",
    iconClassName: "text-teal",
  },
  NOTE: {
    icon: Icons.note,
    label: "Заметка",
    toneClassName: "border border-yellow/25 bg-yellow/10",
    iconClassName: "text-yellow",
  },
  STREAM: {
    icon: Icons.stream,
    label: "Эфир",
    toneClassName: "border border-pink-500/25 bg-pink-500/10",
    iconClassName: "text-pink-400",
  },
};

export function getSearchVisual(entityType: EntityType, materialKind?: string | null) {
  if (entityType === EntityTypes.MATERIAL && materialKind) {
    return MATERIAL_KIND_VISUALS[materialKind] ?? SEARCH_ENTITY_VISUALS[entityType];
  }
  return SEARCH_ENTITY_VISUALS[entityType] ?? UNKNOWN_SEARCH_VISUAL;
}

export function getSearchEntityVisualByValue(value: string) {
  if (value in SEARCH_ENTITY_VISUALS) {
    return SEARCH_ENTITY_VISUALS[value as EntityType];
  }

  return null;
}

export function getSearchHref(document: SearchEducationDocumentDto) {
  const courseSlug = document.courseSlug;

  switch (document.entityType) {
    case EntityTypes.COURSE:
      return courseSlug ? routes.courseOverview(courseSlug) : null;
    case EntityTypes.MODULE:
      return courseSlug && document.moduleId
        ? routes.courseModule(courseSlug, document.moduleId)
        : null;
    case EntityTypes.PROJECT:
      return courseSlug && document.projectId
        ? routes.courseProject(courseSlug, document.projectId)
        : null;
    case EntityTypes.MATERIAL:
      return courseSlug
        ? routes.courseMaterial(courseSlug, document.entityId)
        : routes.materialDetail(document.entityId);
    case EntityTypes.ISSUE:
      return courseSlug ? routes.courseIssue(courseSlug, document.entityId) : null;
    case EntityTypes.COLLECTION:
      return courseSlug
        ? routes.courseCollectionDetail(courseSlug, document.entityId)
        : routes.collectionDetail(document.entityId);
    default:
      return null;
  }
}

export function getSearchBreadcrumbs(document: SearchEducationDocumentDto) {
  const items: BreadcrumbItem[] = [];

  if (document.courseTitle) {
    items.push({
      label: document.courseTitle,
      href: document.courseSlug ? routes.courseOverview(document.courseSlug) : undefined,
    });
  }

  const section = getSectionBreadcrumb(document);
  if (section) {
    items.push(section);
  }

  items.push({ label: document.title });

  return items;
}
