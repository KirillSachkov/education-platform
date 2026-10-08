import { describe, expect, it } from "vitest";
import {
  categoryOf,
  COMMENT_NOTIFICATION_TYPES,
  NotificationCategories,
  typesInCategory,
} from "../model/categories";
import { NotificationTypeLabels, NotificationTypes } from "../model/types";

describe("notification categories", () => {
  it("maps comment notification types to the Comments category", () => {
    expect(categoryOf(NotificationTypes.CommentReplied)).toBe(NotificationCategories.Comments);
    expect(categoryOf(NotificationTypes.CommentOnOwnContent)).toBe(NotificationCategories.Comments);
  });

  it("maps issue submission types to Submissions", () => {
    expect(categoryOf(NotificationTypes.IssueSubmissionApproved)).toBe(
      NotificationCategories.Submissions,
    );
    expect(categoryOf(NotificationTypes.IssueSubmissionChangesRequested)).toBe(
      NotificationCategories.Submissions,
    );
    expect(categoryOf(NotificationTypes.IssueSubmissionAwaitingReview)).toBe(
      NotificationCategories.Submissions,
    );
    expect(categoryOf(NotificationTypes.IssueCreated)).toBe(NotificationCategories.Submissions);
  });

  it("maps Welcome and TelegramLinked to Account", () => {
    expect(categoryOf(NotificationTypes.Welcome)).toBe(NotificationCategories.Account);
    expect(categoryOf(NotificationTypes.TelegramLinked)).toBe(NotificationCategories.Account);
  });

  it("maps course-related types to Course", () => {
    expect(categoryOf(NotificationTypes.CourseEnrolled)).toBe(NotificationCategories.Course);
    expect(categoryOf(NotificationTypes.MaterialPublished)).toBe(NotificationCategories.Course);
    expect(categoryOf(NotificationTypes.AuthorAnnouncement)).toBe(NotificationCategories.Course);
  });

  it("typesInCategory returns all types in that category", () => {
    const commentsTypes = typesInCategory(NotificationCategories.Comments).sort();
    expect(commentsTypes).toEqual(
      [NotificationTypes.CommentReplied, NotificationTypes.CommentOnOwnContent].sort(),
    );

    const submissions = typesInCategory(NotificationCategories.Submissions);
    expect(submissions).toContain(NotificationTypes.IssueSubmissionApproved);
    expect(submissions).toContain(NotificationTypes.IssueSubmissionAwaitingReview);
    expect(submissions).not.toContain(NotificationTypes.CommentReplied);
  });

  it("COMMENT_NOTIFICATION_TYPES contains exactly the Comments-category types", () => {
    expect(COMMENT_NOTIFICATION_TYPES.sort()).toEqual(
      typesInCategory(NotificationCategories.Comments).sort(),
    );
  });

  it("every type maps to a category (no orphans)", () => {
    for (const type of Object.values(NotificationTypes)) {
      const cat = categoryOf(type);
      expect(cat).toBeDefined();
      expect(typesInCategory(cat)).toContain(type);
    }
  });

  it("every type has a human-readable label (no orphans)", () => {
    for (const type of Object.values(NotificationTypes)) {
      expect(NotificationTypeLabels[type]).toBeTruthy();
    }
  });

  it("resolves VideoAutoProcessingFailed (code 22)", () => {
    expect(NotificationTypes.VideoAutoProcessingFailed).toBe(22);
    expect(categoryOf(NotificationTypes.VideoAutoProcessingFailed)).toBe(
      NotificationCategories.Course,
    );
    expect(NotificationTypeLabels[NotificationTypes.VideoAutoProcessingFailed]).toBe(
      "Обработка видео",
    );
  });

  it("resolves EmailLoginNotice (code 25) into Account", () => {
    expect(NotificationTypes.EmailLoginNotice).toBe(25);
    expect(categoryOf(NotificationTypes.EmailLoginNotice)).toBe(NotificationCategories.Account);
    expect(NotificationTypeLabels[NotificationTypes.EmailLoginNotice]).toBe("Вход по почте");
  });

  it("resolves LinkAccountsNudge (code 26) into Account", () => {
    expect(NotificationTypes.LinkAccountsNudge).toBe(26);
    expect(categoryOf(NotificationTypes.LinkAccountsNudge)).toBe(NotificationCategories.Account);
    expect(NotificationTypeLabels[NotificationTypes.LinkAccountsNudge]).toBe("Привязка аккаунтов");
  });

  it("resolves StudentPrQuestionAsked (code 27) into Submissions", () => {
    expect(NotificationTypes.StudentPrQuestionAsked).toBe(27);
    expect(categoryOf(NotificationTypes.StudentPrQuestionAsked)).toBe(
      NotificationCategories.Submissions,
    );
    expect(NotificationTypeLabels[NotificationTypes.StudentPrQuestionAsked]).toBe(
      "Вопрос студента по PR",
    );
  });
});
