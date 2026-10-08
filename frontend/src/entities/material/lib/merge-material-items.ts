import { getMaterialSortTimestamp } from "./material-ui";
import type { MaterialSummaryDto } from "../types";

export function mergeMaterialItems(
  ...groups: MaterialSummaryDto[][]
): MaterialSummaryDto[] {
  const deduplicated = [
    ...new Map(
      groups.flat().map((item) => [item.id, item] as const),
    ).values(),
  ];

  return deduplicated.sort((left, right) => {
    const byTimestamp =
      getMaterialSortTimestamp(right) - getMaterialSortTimestamp(left);
    if (byTimestamp !== 0) {
      return byTimestamp;
    }

    return right.id.localeCompare(left.id);
  });
}
