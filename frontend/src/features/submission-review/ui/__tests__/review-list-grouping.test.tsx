import { describe, it, expect } from "vitest";
import { groupBySubmitter } from "../review-list";
import type { ReviewSubmissionItemDto } from "@/entities/review-submission";

function makeItem(overrides: Partial<ReviewSubmissionItemDto> = {}): ReviewSubmissionItemDto {
  return {
    submissionId: "s1",
    courseId: "c1",
    projectId: "p1",
    issueId: "i1",
    studentId: "u1",
    submissionNo: 1,
    attemptsCount: 1,
    payload: "https://github.com/test/repo/pull/1",
    issueProgressStatus: "UNDER_REVIEW",
    reviewStatus: "PENDING",
    reviewerId: null,
    submittedAt: "2026-05-10T10:00:00Z",
    reviewStartedAt: null,
    reviewedAt: null,
    feedback: null,
    studentName: "Alice",
    studentUsername: "alice",
    studentAvatarId: null,
    studentEmail: null,
    studentTelegramUsername: null,
    reviewerName: null,
    reviewerUsername: null,
    reviewerAvatarId: null,
    courseTitle: "Test course",
    projectTitle: "Test project",
    issueTitle: "Test issue",
    latestAiVerdict: null,
    aiIterationsCount: 0,
    lastAiIterationAt: null,
    aiReviewStatus: null,
    authorHelpRequestedAt: null,
    authorHelpMessage: null,
    studentQuestionAt: null,
    ...overrides,
  };
}

describe("groupBySubmitter (#383 — stack attempts per (issue, student))", () => {
  it("collapses 3 attempts of one (issue, student) into a single card", () => {
    const items = [
      makeItem({ submissionId: "a1", issueId: "i1", studentId: "u1", submissionNo: 1 }),
      makeItem({ submissionId: "a2", issueId: "i1", studentId: "u1", submissionNo: 2 }),
      makeItem({ submissionId: "a3", issueId: "i1", studentId: "u1", submissionNo: 3 }),
    ];

    const groups = groupBySubmitter(items);

    expect(groups).toHaveLength(1);
    // The visible card is the LATEST attempt (max submissionNo).
    expect(groups[0].submissionId).toBe("a3");
    expect(groups[0].submissionNo).toBe(3);
  });

  it("keeps two different students on the same issue as two cards", () => {
    const items = [
      makeItem({ submissionId: "a1", issueId: "i1", studentId: "u1" }),
      makeItem({ submissionId: "b1", issueId: "i1", studentId: "u2" }),
    ];

    const groups = groupBySubmitter(items);

    expect(groups).toHaveLength(2);
    expect(new Set(groups.map((g) => g.studentId))).toEqual(new Set(["u1", "u2"]));
  });

  it("keeps the same student on two different issues as two cards", () => {
    const items = [
      makeItem({ submissionId: "a1", issueId: "i1", studentId: "u1" }),
      makeItem({ submissionId: "b1", issueId: "i2", studentId: "u1" }),
    ];

    const groups = groupBySubmitter(items);

    expect(groups).toHaveLength(2);
    expect(new Set(groups.map((g) => g.issueId))).toEqual(new Set(["i1", "i2"]));
  });

  it("picks the latest attempt regardless of input order", () => {
    const items = [
      makeItem({ submissionId: "a3", issueId: "i1", studentId: "u1", submissionNo: 3 }),
      makeItem({ submissionId: "a1", issueId: "i1", studentId: "u1", submissionNo: 1 }),
      makeItem({ submissionId: "a2", issueId: "i1", studentId: "u1", submissionNo: 2 }),
    ];

    const groups = groupBySubmitter(items);

    expect(groups).toHaveLength(1);
    expect(groups[0].submissionNo).toBe(3);
  });

  it("preserves first-seen order across groups (keyset pagination order)", () => {
    const items = [
      makeItem({ submissionId: "z", issueId: "i9", studentId: "u9" }),
      makeItem({ submissionId: "a", issueId: "i1", studentId: "u1" }),
      makeItem({ submissionId: "m", issueId: "i5", studentId: "u5" }),
    ];

    const groups = groupBySubmitter(items);

    expect(groups.map((g) => g.issueId)).toEqual(["i9", "i1", "i5"]);
  });

  it("returns a new array and does not mutate the input (React Compiler safety)", () => {
    const items = [makeItem({ submissionId: "a1" })];
    const groups = groupBySubmitter(items);

    expect(groups).not.toBe(items);
    expect(items).toHaveLength(1);
  });

  it("returns an empty array for empty input", () => {
    expect(groupBySubmitter([])).toEqual([]);
  });
});
