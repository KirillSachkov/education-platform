"use client";

import { Button } from "@/shared/ui/kit/button";
import { usePublishProject } from "../model/use-publish-project";

interface ProjectStatusActionsProps {
  status: string;
  projectId: string;
  courseId: string;
}

export function ProjectStatusActions({
  status,
  projectId,
  courseId,
}: ProjectStatusActionsProps) {
  const { publishProject, isPending } = usePublishProject(courseId);

  if (status !== "DRAFT") return null;

  return (
    <Button
      type="button"
      size="sm"
      className="bg-orange text-primary-foreground hover:bg-orange/90"
      disabled={isPending}
      onClick={() => publishProject(projectId)}
    >
      {isPending ? "..." : "Опубликовать"}
    </Button>
  );
}
