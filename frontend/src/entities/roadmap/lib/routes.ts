import { routes } from "@/shared/config/routes";

export function getEntityRoute(
  courseSlug: string,
  entityType: string,
  entityId: string,
): string | null {
  switch (entityType) {
    case "Course":
      return routes.courseOverview(entityId);
    case "Material":
      return routes.courseMaterial(courseSlug, entityId);
    case "Issue":
      return routes.courseIssue(courseSlug, entityId);
    case "Module":
      return routes.courseModule(courseSlug, entityId);
    case "Project":
      return routes.courseProject(courseSlug, entityId);
    default:
      return null;
  }
}
