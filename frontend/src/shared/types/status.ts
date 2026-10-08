export type PublicationStatus =
  | "DRAFT"
  | "PUBLISHED"
  | "SUSPENDED"
  | "ARCHIVED";

export type EnrollmentRequestStatus = "PENDING" | "APPROVED" | "REJECTED";

// Progress-related statuses live here (rather than entities/course-progress)
// so shared UI components (StatusBadge, ItemProgressIndicator) can consume
// them without creating an upward FSD dependency.
export type MaterialProgressStatus = "NOT_VIEWED" | "VIEWED";

export type IssueProgressStatus =
  | "NOT_STARTED"
  | "IN_PROGRESS"
  | "UNDER_REVIEW"
  | "REQUESTED_CHANGES"
  | "COMPLETED";

export type SubmissionReviewStatus =
  | "PENDING"
  | "IN_REVIEW"
  | "APPROVED"
  | "CHANGES_REQUESTED";

/**
 * Backend `access.orders.status`. Mirror C# `OrderStatus` enum.
 * Terminal: PAID, FAILED, REFUNDED. Non-terminal (polling continues): PENDING.
 *
 * Lives в shared/types (а не в entities/access-order) чтобы entities/access-admin-order
 * мог импортировать без cross-entity import (FSD boundaries error).
 */
export type OrderStatus = "PENDING" | "PAID" | "FAILED" | "REFUNDED";
