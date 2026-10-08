export const VIEW_PRIORITIES = ["Key", "Recommended", "Supplementary"] as const;
export type ViewPriority = (typeof VIEW_PRIORITIES)[number];

export const VIEW_PRIORITY_LABELS: Record<ViewPriority, string> = {
  Key: "Ключевой",
  Recommended: "Рекомендуемый",
  Supplementary: "Доп.",
};
