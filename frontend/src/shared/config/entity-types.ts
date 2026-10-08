export const EntityTypes = {
  COURSE: "course",
  MODULE: "module",
  MATERIAL: "material",
  ISSUE: "issue",
  PROJECT: "project",
  QUIZ: "quiz",
  COLLECTION: "collection",
} as const;

export type EntityType = (typeof EntityTypes)[keyof typeof EntityTypes];
