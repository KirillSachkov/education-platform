"use client";

import { Button } from "@/shared/ui/kit/button";
import { usePublishModule } from "../model/use-publish-module";

interface ModuleStatusActionsProps {
  status: string;
  moduleId: string;
  courseId: string;
}

export function ModuleStatusActions({
  status,
  moduleId,
  courseId,
}: ModuleStatusActionsProps) {
  const { publishModule, isPending } = usePublishModule(courseId);

  if (status !== "DRAFT") return null;

  return (
    <Button
      type="button"
      size="sm"
      className="bg-teal text-primary-foreground hover:bg-teal/90"
      disabled={isPending}
      onClick={() => publishModule(moduleId)}
    >
      {isPending ? "..." : "Опубликовать"}
    </Button>
  );
}
