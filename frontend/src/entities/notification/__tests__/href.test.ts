import { describe, expect, it } from "vitest";
import { notificationHref } from "../model/href";
import { NotificationTypes, type Notification } from "../model/types";

function makeNotification(overrides?: Partial<Notification>): Notification {
  return {
    id: "n1",
    type: NotificationTypes.CourseEnrolled,
    templateId: "course.enrolled",
    title: "title",
    body: "body",
    payload: "{}",
    targetUrl: "/@sachkov/courses/course-1",
    channels: 1,
    createdAt: new Date().toISOString(),
    readAt: null,
    correlationId: null,
    ...overrides,
  };
}

describe("notificationHref", () => {
  it("returns dto targetUrl", () => {
    const n = makeNotification({
      payload: JSON.stringify({ courseId: "abc" }),
      targetUrl: "/@sachkov/courses/abc",
    });
    expect(notificationHref(n)).toBe("/@sachkov/courses/abc");
  });

  it("returns null when targetUrl missing — consumer opens the detail dialog, not '/' (#708)", () => {
    const n = makeNotification({ targetUrl: "" });
    expect(notificationHref(n)).toBeNull();
  });

  it("preserves comment anchors in targetUrl", () => {
    const n = makeNotification({
      type: NotificationTypes.CommentReplied,
      targetUrl: "/@sachkov/courses/c1/learn/m1?comment=abc",
    });
    expect(notificationHref(n)).toBe("/@sachkov/courses/c1/learn/m1?comment=abc");
  });
});
