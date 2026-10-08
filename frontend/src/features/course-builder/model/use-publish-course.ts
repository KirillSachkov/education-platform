// Re-export from entity layer — this hook manipulates the course aggregate
// and is shared across features (author-courses + course-builder).
export { usePublishCourse } from "@/entities/course";
