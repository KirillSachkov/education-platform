import type { AccessType } from "@/shared/config/access-type";

export interface ModuleItemDto {
  id: string;
  referenceId: string;
  itemType: string;
  sortKey: string;
  isOptional: boolean;
  viewPriority: string;
  title: string | null;
  status: string | null;
  accessType: AccessType | null;
  // Заполняются только в course-builder контексте и только для видео-материалов.
  // В остальных местах (curriculum, module detail) приходят `null` и не рендерятся.
  hasTranscript?: boolean | null;
  hasTimecodes?: boolean | null;
  hasSummary?: boolean | null;
  // Только course-builder: manual ImageId → fallback на Kinescope thumbnail.
  coverUrl?: string | null;
  // Только для item_type='Quiz' (ST-12 #492): число вопросов + id квиза.
  questionsCount?: number | null;
  quizId?: string | null;
}

export interface ModuleDetailDto {
  id: string;
  authorId: string;
  title: string;
  description: string | null;
  detailedDescription: string | null;
  status: string;
  imageId: string | null;
  createdAt: string;
  updatedAt: string;
  items: ModuleItemDto[];
}

export interface UpdateModuleRequest {
  title: string;
  description: string | null;
  detailedDescription?: string | null;
}

export interface ModuleOverviewItemDto {
  id: string;
  itemType: string;
  title: string;
  isOptional: boolean;
  viewPriority: string;
}

export interface ModuleOverviewDto {
  id: string;
  title: string;
  description: string | null;
  detailedDescription: string | null;
  lessonCount: number;
  issueCount: number;
  items: ModuleOverviewItemDto[];
}

export interface CreateModuleLessonRequest {
  title: string;
  content?: string;
}

export interface MoveModuleItemRequest {
  afterSortKey?: string;
  beforeSortKey?: string;
}

export interface AttachMaterialToModuleRequest {
  materialId: string;
}

/** `POST /modules/{moduleId}/quizzes/` — привязка квиза к модулю (ST-12 #492). */
export interface AttachQuizToModuleRequest {
  quizId: string;
}

export interface TransferModuleItemRequest {
  targetModuleId: string;
  afterSortKey?: string;
  beforeSortKey?: string;
}

export interface UpdateLessonRequest {
  title: string;
  content?: string;
  accessType: AccessType;
}
