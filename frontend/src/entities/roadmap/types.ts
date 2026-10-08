export type RoadmapId = string;

export type RoadmapStatus = "DRAFT" | "PUBLISHED" | "ARCHIVED";

export type RoadmapNodeType =
  | "EntityReference"
  | "TextNote"
  | "ExternalLink"
  | "Group";

export type EntityReferenceType =
  | "Course"
  | "Module"
  | "Material"
  | "Project"
  | "Issue"
  | "Quiz";

// --- Node data payloads (stored as JSON string in `data` field) ---

export interface EntityReferenceData {
  entityType: EntityReferenceType;
  entityId: string;
  courseId?: string;
  entityTitle?: string | null;
  entityDescription?: string | null;
  entityStatus?: string | null;
  imageId?: string | null;
}

export interface TextNoteData {
  text: string;
  fontSize?: number;
  backgroundColor?: string;
}

export interface ExternalLinkData {
  url: string;
  title: string;
  description?: string;
}

export interface GroupData {
  label: string;
  backgroundColor?: string;
}

// --- DTOs from backend ---

export interface RoadmapNodeDto {
  id: string;
  nodeType: RoadmapNodeType;
  positionX: number;
  positionY: number;
  width: number | null;
  height: number | null;
  parentNodeId: string | null;
  data: string;
  sortOrder: number;
}

export interface RoadmapEdgeDto {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  label: string | null;
  edgeType: string;
  animated: boolean;
  sourceHandle: string | null;
  targetHandle: string | null;
}

export interface RoadmapDto {
  id: RoadmapId;
  authorId: string;
  title: string;
  description: string | null;
  courseId: string | null;
  slug: string | null;
  status: RoadmapStatus;
  createdAt: string;
  updatedAt: string;
  nodes: RoadmapNodeDto[];
  edges: RoadmapEdgeDto[];
}

export interface RoadmapSummaryDto {
  id: RoadmapId;
  authorId: string;
  title: string;
  description: string | null;
  courseId: string | null;
  slug: string | null;
  status: RoadmapStatus;
  nodeCount: number;
  createdAt: string;
  updatedAt: string;
}

// --- Requests ---

export interface CreateRoadmapRequest {
  title: string;
  description?: string | null;
  courseId?: string | null;
  slug?: string | null;
}

export interface UpdateRoadmapRequest {
  title: string;
  description?: string | null;
  slug?: string | null;
}

export interface SaveCanvasNodeDto {
  id: string;
  nodeType: string;
  positionX: number;
  positionY: number;
  width: number | null;
  height: number | null;
  parentNodeId: string | null;
  data: string;
  sortOrder: number;
}

export interface SaveCanvasEdgeDto {
  id: string;
  sourceNodeId: string;
  targetNodeId: string;
  label: string | null;
  edgeType: string;
  animated: boolean;
  sourceHandle: string | null;
  targetHandle: string | null;
}

export interface SaveCanvasRequest {
  nodes: SaveCanvasNodeDto[];
  edges: SaveCanvasEdgeDto[];
}

// --- Progress ---

export interface RoadmapProgressItemRequest {
  entityType: string;
  entityId: string;
  courseId?: string | null;
}

export interface RoadmapProgressItemDto {
  entityType: string;
  entityId: string;
  courseId: string | null;
  status: string;
  completedAt: string | null;
}

export interface RoadmapProgressResponse {
  items: RoadmapProgressItemDto[];
  enrolledCourseIds: string[];
}
