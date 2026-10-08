export type AiModelSettingsSource = "CONFIG" | "DATABASE";

export interface AssignmentReviewAiSlotDto {
  model: string;
  temperature: number | null;
  maxOutputTokens: number | null;
  timeoutSeconds: number | null;
  source: AiModelSettingsSource;
}

export interface ReviewerBasePromptEffective {
  value: string;
  source: AiModelSettingsSource;
}

export interface AssignmentReviewAiSettingsDto {
  reviewer: AssignmentReviewAiSlotDto;
  reviewerBasePrompt: ReviewerBasePromptEffective;
  /** Platform-wide master switch for AI PR review (#355). Default false. */
  reviewEnabled: boolean;
  /** Reviewer may request repository files via need_files loop (#798). Default false. */
  repoContextEnabled: boolean;
  updatedAt: string | null;
  updatedByUserId: string | null;
}

export interface UpdateAssignmentReviewAiSettingsRequest {
  reviewer: AssignmentReviewAiSlotInput;
  /** null/empty resets to the config default base prompt. */
  reviewerBasePrompt?: string | null;
  /** Platform-wide master switch for AI PR review (#355). */
  reviewEnabled: boolean;
  /** Reviewer may request repository files via need_files loop (#798). */
  repoContextEnabled: boolean;
}

export interface AssignmentReviewAiSlotInput {
  model: string;
  temperature: number | null;
  maxOutputTokens: number | null;
  timeoutSeconds: number | null;
}
