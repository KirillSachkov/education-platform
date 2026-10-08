import { EmptyState } from "@/shared/ui/kit/empty-state";
import { Icons } from "@/shared/ui/icons";

export function BookmarksEmptyState() {
  return (
    <EmptyState
      variant="card"
      icon={Icons.bookmark}
      title="Закладок пока нет"
      description="Сохраняйте уроки и задачи, чтобы быстро возвращаться к ним позже."
    />
  );
}
