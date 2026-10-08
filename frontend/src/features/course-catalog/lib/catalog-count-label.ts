import type { CourseKind } from "@/shared/config/course-kind";
import { pluralize } from "@/shared/lib/pluralize";

/**
 * Подпись счётчика каталога зависит от активного типа (#640): «Все» → нейтральное
 * «N программ» (курсы + интенсивы + марафоны не зовём «курсами»), конкретный тип →
 * своё существительное в правильной форме.
 */
export function catalogCountLabel(count: number, kind: CourseKind | "all"): string {
  switch (kind) {
    case "COURSE":
      return `${count} ${pluralize(count, "курс", "курса", "курсов")}`;
    case "INTENSIVE":
      return `${count} ${pluralize(count, "интенсив", "интенсива", "интенсивов")}`;
    case "MARATHON":
      return `${count} ${pluralize(count, "марафон", "марафона", "марафонов")}`;
    default:
      return `${count} ${pluralize(count, "программа", "программы", "программ")}`;
  }
}
