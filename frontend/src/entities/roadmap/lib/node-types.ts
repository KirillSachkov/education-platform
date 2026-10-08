import type {
  EntityReferenceData,
  ExternalLinkData,
  GroupData,
  RoadmapNodeDto,
  TextNoteData,
} from "../types";

export function safeParseNodeData<T>(rawData: string, fallback: T): T {
  try {
    return JSON.parse(rawData) as T;
  } catch {
    return fallback;
  }
}

export function parseNodeData(node: RoadmapNodeDto) {
  switch (node.nodeType) {
    case "EntityReference":
      return JSON.parse(node.data) as EntityReferenceData;
    case "TextNote":
      return JSON.parse(node.data) as TextNoteData;
    case "ExternalLink":
      return JSON.parse(node.data) as ExternalLinkData;
    case "Group":
      return JSON.parse(node.data) as GroupData;
    default:
      return JSON.parse(node.data) as Record<string, unknown>;
  }
}

export function isEntityReference(
  node: RoadmapNodeDto,
): node is RoadmapNodeDto & { nodeType: "EntityReference" } {
  return node.nodeType === "EntityReference";
}

export function isGroup(
  node: RoadmapNodeDto,
): node is RoadmapNodeDto & { nodeType: "Group" } {
  return node.nodeType === "Group";
}

export const NODE_TYPE_LABELS: Record<string, string> = {
  EntityReference: "Ссылка на материал",
  TextNote: "Заметка",
  ExternalLink: "Внешняя ссылка",
  Group: "Группа",
};

export const ENTITY_TYPE_LABELS: Record<string, string> = {
  Course: "Курс",
  Module: "Модуль",
  Lesson: "Урок",
  Project: "Проект",
  Issue: "Задача",
  Article: "Статья",
  Quiz: "Тест",
};
