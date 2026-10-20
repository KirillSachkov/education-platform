import type { ToolDefinition } from '../tool.js';
import { eduPing } from './ping.js';
import {
  eduCourseListAdmin,
  eduCourseCurriculum,
  eduCourseBuilder,
  eduCourseDetail,
  eduCoursePublish,
  eduCourseArchive,
  eduCourseRestore,
  eduCourseReassignAuthor,
} from './courses.js';
import {
  eduCourseListPendingListing,
  eduCourseSetCatalogListing,
} from './catalog-listing.js';
import {
  eduCourseItemDetach,
  eduCourseItemMove,
  eduCourseCreateModule,
  eduCourseCreateProject,
} from './course-items.js';
import {
  eduModuleDetail,
  eduModuleOverview,
  eduModuleUpdate,
  eduModulePublish,
  eduModuleArchive,
  eduModuleRestore,
} from './modules.js';
import {
  eduModuleItemAttachMaterial,
  eduModuleItemAttachIssue,
  eduModuleItemAttachQuiz,
  eduModuleItemDetach,
  eduModuleItemMove,
} from './module-items.js';
import {
  eduQuizList,
  eduQuizDetail,
  eduQuizCreate,
  eduQuizUpdate,
  eduQuizPublish,
  eduQuizDelete,
} from './quizzes.js';
import {
  eduMaterialDetail,
  eduMaterialDelete,
  eduMaterialUpdate,
  eduMaterialArchive,
  eduMaterialCreate,
  eduMaterialPublish,
} from './materials.js';
import {
  eduCollectionDetail,
  eduCollectionSectionAdd,
  eduCollectionSectionAddItem,
  eduCollectionBulkSetItemsAccess,
} from './collections.js';
import { eduFileUpload, eduDraftAssetsBind } from './files.js';
import { eduVideoSubtitlesExport, eduModuleTranscriptsExport } from './transcripts.js';
import {
  eduProjectDetail,
  eduProjectUpdate,
  eduProjectPublish,
  eduProjectArchive,
  eduProjectRestore,
} from './projects.js';
import {
  eduIssueDetail,
  eduIssueCreate,
  eduIssueUpdate,
  eduIssueDelete,
  eduIssueArchive,
  eduIssuePublish,
  eduIssueRestore,
  eduProjectIssueAttach,
  eduProjectIssueDetach,
  eduProjectIssueMove,
  eduIssueSetInternalMaterials,
  eduIssueListAdmin,
  eduIssueSearchAdmin,
  eduCourseIssuesExport,
  eduIssueUpdateDryRun,
} from './issues.js';
import {
  eduAiReviewSettingsGet,
  eduAiReviewSettingsSet,
  eduProjectSetReviewContext,
  eduIssueSetReviewSpec,
  eduProjectReviewContextGet,
  eduIssueReviewSpecGet,
  eduProjectReviewCoverage,
  eduCourseReviewCoverage,
} from './ai-review.js';
import {
  homePinsList,
  homePinsAdd,
  homePinsUpdateNote,
  homePinsReorder,
  homePinsRemove,
  onboardingResetAll,
} from './home-pins.js';
import {
  eduUserList,
  eduUserDetail,
  eduUserStats,
  eduUserSetRoles,
  eduUserLockout,
} from './users.js';
import {
  eduPlanList,
  eduPlanDelete,
  eduPlanSetTelegramWelcome,
  eduPlanGrantsList,
  eduUserPostPurchaseStatus,
  eduUserRecheckTelegramMembership,
  eduUserRecheckGithubMembership,
  eduGrantIssue,
  eduGrantRevoke,
  eduTrialCreditOverride,
  eduInviteList,
  eduInviteCreate,
  eduInviteRevoke,
} from './access.js';
import {
  eduUserTelegramLink,
  eduPlanTelegramChats,
  eduUserResendPlanWelcome,
  eduUserResendTelegramInvites,
} from './telegram-support.js';
import {
  eduCourseStudents,
  eduStudentProgress,
  eduIssueMarkCompleteForUser,
  eduIssueSetStatusForUser,
  eduMaterialMarkViewedForUser,
} from './student-progress.js';

export const allTools: ReadonlyArray<ToolDefinition<any, any>> = [
  // Connectivity
  eduPing,
  // Courses
  eduCourseListAdmin,
  eduCourseCurriculum,
  eduCourseBuilder,
  eduCourseDetail,
  eduCoursePublish,
  eduCourseArchive,
  eduCourseRestore,
  // Catalog moderation (#569 "Второй автор"): pending-listing queue + approve/hide toggle
  eduCourseListPendingListing,
  eduCourseSetCatalogListing,
  // Ownership transfer (#587): reassign a course + its exclusive content to another author
  eduCourseReassignAuthor,
  // Course items
  eduCourseItemDetach,
  eduCourseItemMove,
  eduCourseCreateModule,
  eduCourseCreateProject,
  // Modules
  eduModuleDetail,
  eduModuleOverview,
  eduModuleUpdate,
  eduModulePublish,
  eduModuleArchive,
  eduModuleRestore,
  // Module items
  eduModuleItemAttachMaterial,
  eduModuleItemAttachIssue,
  eduModuleItemAttachQuiz,
  eduModuleItemDetach,
  eduModuleItemMove,
  // Quizzes (#544): author CRUD + publish/delete (confirm-guarded)
  eduQuizList,
  eduQuizDetail,
  eduQuizCreate,
  eduQuizUpdate,
  eduQuizPublish,
  eduQuizDelete,
  // Materials
  eduMaterialDetail,
  eduMaterialCreate,
  eduMaterialDelete,
  eduMaterialUpdate,
  eduMaterialArchive,
  eduMaterialPublish,
  eduVideoSubtitlesExport,
  eduModuleTranscriptsExport,
  // Collections
  eduCollectionDetail,
  eduCollectionSectionAdd,
  eduCollectionSectionAddItem,
  eduCollectionBulkSetItemsAccess,
  // Files (asset registry)
  eduFileUpload,
  eduDraftAssetsBind,
  // Projects
  eduProjectDetail,
  eduProjectUpdate,
  eduProjectPublish,
  eduProjectArchive,
  eduProjectRestore,
  // Issues / project items
  eduIssueDetail,
  eduIssueCreate,
  eduIssueUpdate,
  eduIssueDelete,
  eduIssueArchive,
  eduIssuePublish,
  eduIssueRestore,
  eduProjectIssueAttach,
  eduProjectIssueDetach,
  eduProjectIssueMove,
  eduIssueSetInternalMaterials,
  // Issue audit / batch tooling (#339): list / search / export / dry-run
  eduIssueListAdmin,
  eduIssueSearchAdmin,
  eduCourseIssuesExport,
  eduIssueUpdateDryRun,
  // AI review prompts (#334)
  eduAiReviewSettingsGet,
  eduAiReviewSettingsSet,
  eduProjectSetReviewContext,
  eduIssueSetReviewSpec,
  // Review config reads (#339): inspect before overwrite
  eduProjectReviewContextGet,
  eduIssueReviewSpecGet,
  // Bulk review-prompt coverage (#356): which tasks still lack prompts
  eduProjectReviewCoverage,
  eduCourseReviewCoverage,
  // Plan home pins + onboarding bulk-reset (epic #397)
  homePinsList,
  homePinsAdd,
  homePinsUpdateNote,
  homePinsReorder,
  homePinsRemove,
  onboardingResetAll,
  // Users (AuthService admin): list / detail / stats + destructive set-roles / lockout (#398)
  eduUserList,
  eduUserDetail,
  eduUserStats,
  eduUserSetRoles,
  eduUserLockout,
  // Access (AccessService): plans + grants + invite-links — entitlement layer (#398)
  eduPlanList,
  eduPlanDelete,
  eduPlanSetTelegramWelcome,
  eduPlanGrantsList,
  eduUserPostPurchaseStatus,
  eduUserRecheckTelegramMembership,
  eduUserRecheckGithubMembership,
  eduGrantIssue,
  eduGrantRevoke,
  // Trial-month credit override (#580): reopen the upgrade-credit window for a user's trial grant
  eduTrialCreditOverride,
  eduInviteList,
  eduInviteCreate,
  eduInviteRevoke,
  // Telegram support (TelegramBotService): link lookup / plan chats / resend welcome + invites (#444)
  eduUserTelegramLink,
  eduPlanTelegramChats,
  eduUserResendPlanWelcome,
  eduUserResendTelegramInvites,
  // Student progress (ProgressService): roster / per-student detail + staff mark-complete overrides (#398) + full status override (#518)
  eduCourseStudents,
  eduStudentProgress,
  eduIssueMarkCompleteForUser,
  eduIssueSetStatusForUser,
  eduMaterialMarkViewedForUser,
];
